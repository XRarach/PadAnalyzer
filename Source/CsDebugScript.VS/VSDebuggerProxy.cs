﻿using CsDebugScript.Engine.Utility;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using DIA;
using System.Runtime.InteropServices;
using CsDebugScript.Engine;

namespace CsDebugScript.VS
{
    /// <summary>
    /// Visual Studio Debugger Proxy object to default AppDomain
    /// </summary>
    /// <seealso cref="System.MarshalByRefObject" />
    internal class VSDebuggerProxy : MarshalByRefObject
    {
        public const string AppDomainDataName = "VSDebuggerProxy.Value";

        private struct ThreadStruct
        {
            public DkmThread Thread;
            public SimpleCache<DkmStackFrame[]> Frames;
        }

        /// <summary>
        /// The cached DKM processes
        /// </summary>
        private List<DkmProcess> processes = new List<DkmProcess>();

        /// <summary>
        /// The cached DKM threads
        /// </summary>
        private List<ThreadStruct> threads = new List<ThreadStruct>();

        /// <summary>
        /// The cached DKM modules
        /// </summary>
        private List<DkmModuleInstance> modules = new List<DkmModuleInstance>();

        /// <summary>
        /// The DKM component manager initialization for the thread
        /// </summary>
        private static System.Threading.ThreadLocal<bool> initializationForThread;

        /// <summary>
        /// Initializes the <see cref="VSDebuggerProxy"/> class.
        /// </summary>
        static VSDebuggerProxy()
        {
            initializationForThread = new System.Threading.ThreadLocal<bool>(() =>
            {
                try
                {
                    DkmComponentManager.InitializeThread(DkmComponentManager.IdeComponentId);
                    return true;
                }
                catch (DkmException ex)
                {
                    if (ex.Code == DkmExceptionCode.E_XAPI_ALREADY_INITIALIZED)
                        return true;
                }
                catch
                {
                }
                return false;
            });
            System.Threading.Thread thread = new System.Threading.Thread(() => StaThreadLoop() );
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VSDebuggerProxy"/> class.
        /// </summary>
        public VSDebuggerProxy()
        {
            processes = new List<DkmProcess>();
            Context.UserTypeMetadata = ScriptCompiler.ExtractMetadata(new[]
            {
                typeof(CsDebugScript.CommonUserTypes.NativeTypes.cv.Mat).Assembly, // CsDebugScript.CommonUserTypes.dll
            });
        }

        /// <summary>
        /// Gets the cached DKM processes.
        /// </summary>
        internal List<DkmProcess> Processes
        {
            get
            {
                if (processes.Count == 0)
                {
                    ExecuteOnDkmInitializedThread(() =>
                    {
                        processes.AddRange(DkmProcess.GetProcesses());
                    });
                }

                return processes;
            }
        }

        #region Debugger functionality
        public bool IsProcessLiveDebugging(uint processId)
        {
            DkmProcess process = GetProcess(processId);

            return (process.SystemInformation.Flags & Microsoft.VisualStudio.Debugger.DefaultPort.DkmSystemInformationFlags.DumpFile) == 0;
        }

        public int[] GetAllProcesses()
        {
            return Processes.Select(p => p?.LivePart?.Id ?? 0).ToArray();
        }

        public int GetCurrentProcessSystemId()
        {
            return VSContext.DTE.Debugger.CurrentProcess.ProcessID;
        }

        public int GetCurrentThreadSystemId()
        {
            return VSContext.DTE.Debugger.CurrentThread.ID;
        }

        public int GetCurrentStackFrameNumber(int threadId)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;

            DkmStackWalkFrame frame = dispatcher.Invoke(() =>
            {
                var stackFrame = VSContext.DTE.Debugger.CurrentStackFrame;

                return DkmStackFrame.ExtractFromDTEObject(stackFrame);
            });
            DkmStackWalkFrame[] frames = threads[threadId].Frames.Value;

            for (int i = 0; i < frames.Length; i++)
                if (frames[i].FrameBase == frame.FrameBase)
                    return i;
            return -1;
        }

        public ulong GetModuleAddress(uint processId, string moduleName)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);

                moduleName = moduleName.ToLower();
                return process.GetRuntimeInstances().SelectMany(r => r.GetModuleInstances()).Where(m => GetModuleName(m).ToLower() == moduleName).Single().BaseAddress;
            });
        }

        public string GetModuleImageName(uint moduleId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmModuleInstance module = GetModule(moduleId);

                return module.FullName;
            });
        }

        public string GetModuleName(uint moduleId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmModuleInstance module = GetModule(moduleId);

                return GetModuleName(module);
            });
        }

        public string GetModuleSymbolName(uint moduleId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmModuleInstance module = GetModule(moduleId);
                DkmSymbolFileId symbolFileId = module.SymbolFileId;

                if (symbolFileId.TagValue == DkmSymbolFileId.Tag.PdbFileId)
                {
                    DkmPdbFileId pdbFileId = (DkmPdbFileId)symbolFileId;

                    return pdbFileId.PdbName;
                }

                return module.FullName;
            });
        }

        public object GetModuleDiaSession(uint moduleId)
        {
            Func<object> executor = () =>
            {
                try
                {
                    DkmModuleInstance module = GetModule(moduleId);

                    return module.Module.GetSymbolInterface(Marshal.GenerateGuidForType(typeof(IDiaSession)));
                }
                catch
                {
                    return null;
                }
            };

            object result = executor();

            if (result == null)
            {
                result = ExecuteOnDkmInitializedThread(executor);
            }
            return result;
        }

        public Tuple<DateTime, ulong> GetModuleTimestampAndSize(uint moduleId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmModuleInstance module = GetModule(moduleId);

                return Tuple.Create(DateTime.FromFileTimeUtc((long)module.TimeDateStamp), (ulong)module.Size);
            });
        }

        public void GetModuleVersion(uint moduleId, out int major, out int minor, out int revision, out int patch)
        {
            int tempMajor = 0, tempMinor = 0, tempRevision = 0, tempPatch = 0;

            ExecuteOnDkmInitializedThread(() =>
            {
                DkmModuleInstance module = GetModule(moduleId);

                if (module.Version != null)
                {
                    tempMajor = (int)(module.Version.ProductVersionMS / 65536);
                    tempMinor = (int)(module.Version.ProductVersionMS % 65536);
                    tempRevision = (int)(module.Version.ProductVersionLS / 65536);
                    tempPatch = (int)(module.Version.ProductVersionLS % 65536);
                }
                else
                {
                    tempMajor = tempMinor = tempRevision = tempPatch = 0;
                }
            });

            major = tempMajor;
            minor = tempMinor;
            revision = tempRevision;
            patch = tempPatch;
        }

        public ArchitectureType GetProcessArchitectureType(uint processId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);

                switch (process.SystemInformation.ProcessorArchitecture)
                {
                    case DkmProcessorArchitecture.PROCESSOR_ARCHITECTURE_INTEL:
                    case DkmProcessorArchitecture.PROCESSOR_ARCHITECTURE_AMD64:
                        return (process.SystemInformation.Flags & Microsoft.VisualStudio.Debugger.DefaultPort.DkmSystemInformationFlags.Is64Bit) != 0
                            ? ArchitectureType.Amd64 : ArchitectureType.X86OverAmd64;
                    case DkmProcessorArchitecture.PROCESSOR_ARCHITECTURE_ARM:
                        return ArchitectureType.Arm;
                    default:
                        throw new NotImplementedException("Unexpected DkmProcessorArchitecture");
                }
            });
        }

        public string GetProcessDumpFileName(uint processId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);

                if (process.LivePart == null)
                {
                    return process.Path;
                }

                return string.Empty;
            });
        }

        public string GetProcessExecutableName(uint processId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);

                return process.Path;
            });
        }

        public Tuple<uint, ulong>[] GetProcessModules(uint processId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);
                var modules = process.GetRuntimeInstances().SelectMany(r => r.GetModuleInstances());
                List<Tuple<uint, ulong>> result = new List<Tuple<uint, ulong>>();

                lock (this.modules)
                {
                    foreach (var module in modules)
                    {
                        result.Add(Tuple.Create((uint)this.modules.Count, module.BaseAddress));
                        this.modules.Add(module);
                    }
                }

                return result.ToArray();
            });
        }

        public uint GetModuleId(uint processId, ulong address)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);
                var module = process.GetRuntimeInstances().SelectMany(r => r.GetModuleInstances()).First(m => m.BaseAddress == address);

                lock (modules)
                {
                    uint id = (uint)modules.Count;
                    modules.Add(module);
                    return id;
                }
            });
        }

        public uint GetProcessSystemId(uint processId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);

                if (process.LivePart != null)
                {
                    return (uint)process.LivePart.Id;
                }

                return (uint)0;
            });
        }

        public Tuple<uint, uint>[] GetProcessThreads(uint processId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);
                DkmThread[] threads = process.GetThreads();
                Tuple<uint, uint>[] result = new Tuple<uint, uint>[threads.Length];

                lock (this.threads)
                {
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = Tuple.Create((uint)this.threads.Count, (uint)threads[i].SystemPart.Id);
                        this.threads.Add(new ThreadStruct()
                        {
                            Thread = threads[i],
                            Frames = new SimpleCache<DkmStackFrame[]>(() =>
                            {
                                throw new NotImplementedException("You should first enumerate process threads!");
                            }),
                        });
                    }
                }

                return result;
            });
        }

        public unsafe void GetThreadContext(uint threadId, IntPtr contextBufferPointer, int contextBufferSize)
        {
            ExecuteOnDkmInitializedThread(() =>
            {
                DkmThread thread = GetThread(threadId);
                int flags = 0x1f;

                thread.GetContext(flags, contextBufferPointer.ToPointer(), contextBufferSize);
            });
        }

        public ulong GetThreadEnvironmentBlockAddress(uint threadId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmThread thread = GetThread(threadId);

                return thread.TebAddress;
            });
        }

        public ulong GetRegisterValue(uint threadId, uint frameId, uint registerId)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmFrameRegisters frameRegisters = threads[(int)threadId].Frames.Value[frameId].Registers;
                CV_HREG_e register = (CV_HREG_e)registerId;
                byte[] data = new byte[129];
                int result = frameRegisters.GetRegisterValue(registerId, data);

                if (result > 0)
                {
                    if (result == data.Length)
                    {
                        result = (int)Process.Current.GetPointerSize();
                    }

                    switch (result)
                    {
                        case 8:
                            return BitConverter.ToUInt64(data, 0);
                        case 4:
                            return BitConverter.ToUInt32(data, 0);
                        case 2:
                            return BitConverter.ToUInt16(data, 0);
                        case 1:
                            return data[0];
                        default:
                            throw new NotImplementedException($"Unexpected number of bytes for register value: {result}");
                    }
                }

                switch (frameRegisters.TagValue)
                {
                    case DkmFrameRegisters.Tag.X64Registers:
                        {
                            DkmX64FrameRegisters registers = (DkmX64FrameRegisters)frameRegisters;

                            switch (register)
                            {
                                case CV_HREG_e.CV_AMD64_RIP:
                                    return registers.Rip;
                                case CV_HREG_e.CV_AMD64_RSP:
                                    return registers.Rsp;
                            }
                        }
                        break;
                    case DkmFrameRegisters.Tag.X86Registers:
                        {
                            DkmX86FrameRegisters registers = (DkmX86FrameRegisters)frameRegisters;

                            switch (register)
                            {
                                case CV_HREG_e.CV_REG_EIP:
                                    return registers.Eip;
                                case CV_HREG_e.CV_REG_ESP:
                                    return registers.Esp;
                            }
                        }
                        break;
                    default:
                        throw new NotImplementedException($"Unexpected DkmFrameRegisters.Tag: {frameRegisters.TagValue}");
                }

                for (int i = 0; i < frameRegisters.UnwoundRegisters.Count; i++)
                {
                    if (register == (CV_HREG_e)frameRegisters.UnwoundRegisters[i].Identifier)
                    {
                        byte[] bytes = frameRegisters.UnwoundRegisters[i].Value.ToArray();

                        switch (bytes.Length)
                        {
                            case 8:
                                return BitConverter.ToUInt64(bytes, 0);
                            case 4:
                                return BitConverter.ToUInt32(bytes, 0);
                            case 2:
                                return BitConverter.ToUInt16(bytes, 0);
                            case 1:
                                return bytes[0];
                            default:
                                throw new NotImplementedException($"Unexpected number of bytes for register value: {bytes.Length}");
                        }
                    }
                }

                throw new KeyNotFoundException($"Register not found: {register}");
            });
        }

        public Tuple<ulong, ulong, ulong>[] GetThreadStackTrace(uint threadId, byte[] threadContextBytes)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmThread thread = GetThread(threadId);
                List<DkmStackFrame> frames = new List<DkmStackFrame>();
                DkmProcess process = thread.Process;

                using (DkmInspectionSession dkmInspectionSession = DkmInspectionSession.Create(process, null))
                {
                    using (DkmStackContext dkmStackContext = DkmStackContext.Create(dkmInspectionSession, thread, DkmCallStackFilterOptions.None, new DkmFrameFormatOptions(), new System.Collections.ObjectModel.ReadOnlyCollection<byte>(threadContextBytes), null))
                    {
                        bool done = false;

                        while (!done)
                        {
                            DkmWorkList dkmWorkList = DkmWorkList.Create(null);

                            dkmStackContext.GetNextFrames(dkmWorkList, int.MaxValue, (ar) =>
                            {
                                frames.AddRange(ar.Frames);
                                done = ar.Frames.Length == 0;
                            });

                            dkmWorkList.Execute();
                        }
                    }
                }

                threads[(int)threadId].Frames.Value = frames.ToArray();

                Tuple<ulong, ulong, ulong>[] result = new Tuple<ulong, ulong, ulong>[frames.Count];

                for (int i = 0; i < result.Length; i++)
                {
                    ulong stackOffset, instructionOffset;

                    switch (frames[i].Registers.TagValue)
                    {
                        case DkmFrameRegisters.Tag.X64Registers:
                            {
                                DkmX64FrameRegisters registers = (DkmX64FrameRegisters)frames[i].Registers;

                                instructionOffset = registers.Rip;
                                stackOffset = registers.Rsp;
                            }
                            break;
                        case DkmFrameRegisters.Tag.X86Registers:
                            {
                                DkmX86FrameRegisters registers = (DkmX86FrameRegisters)frames[i].Registers;

                                instructionOffset = registers.Eip;
                                stackOffset = registers.Esp;
                            }
                            break;
                        default:
                            throw new NotImplementedException("Unexpected DkmFrameRegisters.Tag");
                    }

                    bool found = false;
                    ulong frameOffset = 0;

                    for (int j = 0; !found && j < frames[i].Registers.UnwoundRegisters.Count; j++)
                    {
                        switch ((CV_HREG_e)frames[i].Registers.UnwoundRegisters[j].Identifier)
                        {
                            case CV_HREG_e.CV_AMD64_EBP:
                            case CV_HREG_e.CV_AMD64_RBP:
                                {
                                    byte[] bytes = frames[i].Registers.UnwoundRegisters[j].Value.ToArray();

                                    found = true;
                                    frameOffset = bytes.Length == 8 ? BitConverter.ToUInt64(bytes, 0) : BitConverter.ToUInt32(bytes, 0);
                                    break;
                                }
                        }
                    }

                    if (frames[i].InstructionAddress != null
                        && frames[i].InstructionAddress.CPUInstructionPart != null
                        && instructionOffset != frames[i].InstructionAddress.CPUInstructionPart.InstructionPointer)
                    {
                        throw new Exception("Instruction offset is not the same?");
                    }

                    result[i] = Tuple.Create(instructionOffset, stackOffset, frameOffset);
                }

                return result;
            });
        }

        public byte[] ReadMemory(uint processId, ulong address, uint size)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);
                byte[] bytes = new byte[size];

                process.ReadMemory(address, DkmReadMemoryFlags.None, bytes);
                return bytes;
            });
        }

        private static string ReadMemoryString(DkmProcess process, ulong address, int length, ushort charSize, System.Text.Encoding encoding)
        {
            bool trimNullTermination = false;

            if (length < 0)
            {
                length = ushort.MaxValue;
                trimNullTermination = true;
            }

            byte[] bytes = process.ReadMemoryString(address, DkmReadMemoryFlags.None, charSize, length);

            if (trimNullTermination && bytes[bytes.Length - 1] == 0)
            {
                return encoding.GetString(bytes, 0, bytes.Length - 1);
            }

            return encoding.GetString(bytes);
        }

        public string ReadAnsiString(uint processId, ulong address, int length)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);

                return ReadMemoryString(process, address, length, 1, System.Text.ASCIIEncoding.Default);
            });
        }

        public string ReadUnicodeString(uint processId, ulong address, int length)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);

                return ReadMemoryString(process, address, length, 2, System.Text.UnicodeEncoding.Default);
            });
        }

        public string ReadWideUnicodeString(uint processId, ulong address, int length)
        {
            return ExecuteOnDkmInitializedThread(() =>
            {
                DkmProcess process = GetProcess(processId);

                return ReadMemoryString(process, address, length, 4, System.Text.Encoding.UTF32);
            });
        }

        public void SetCurrentProcess(uint processId)
        {
            ExecuteOnDkmInitializedThread(() =>
            {
                uint processSystemId = GetProcessSystemId(processId);

                if (VSContext.DTE.Debugger.CurrentProcess.ProcessID != processSystemId)
                {
                    foreach (EnvDTE.Process vsProcess in VSContext.DTE.Debugger.DebuggedProcesses)
                    {
                        if (processSystemId == vsProcess.ProcessID)
                        {
                            VSContext.DTE.Debugger.CurrentProcess = vsProcess;
                            return;
                        }
                    }

                    throw new ArgumentException("Process wasn't found", nameof(processId));
                }
            });
        }

        public void SetCurrentThread(uint threadId)
        {
            ExecuteOnDkmInitializedThread(() =>
            {
                DkmThread thread = GetThread(threadId);
                int threadSystemId = thread.SystemPart.Id;

                if (VSContext.DTE.Debugger.CurrentThread.ID != threadSystemId)
                {
                    foreach (EnvDTE.Program vsProgram in VSContext.DTE.Debugger.CurrentProcess.Programs)
                    {
                        foreach (EnvDTE.Thread vsThread in vsProgram.Threads)
                        {
                            if (threadSystemId == vsThread.ID)
                            {
                                VSContext.DTE.Debugger.CurrentThread = vsThread;
                                return;
                            }
                        }
                    }

                    throw new ArgumentException("Thread wasn't found", nameof(threadId));
                }
            });
        }

        public void ClearCache()
        {
            processes.Clear();
            threads.Clear();
            modules.Clear();
        }
        #endregion

        /// <summary>
        /// Gets the name of the module.
        /// </summary>
        /// <param name="module">The module.</param>
        private static string GetModuleName(DkmModuleInstance module)
        {
            return System.IO.Path.GetFileNameWithoutExtension(module.Name);
        }

        private DkmProcess GetProcess(uint id)
        {
            lock (processes)
            {
                return Processes[(int)id];
            }
        }

        private DkmThread GetThread(uint id)
        {
            lock (threads)
            {
                return threads[(int)id].Thread;
            }
        }

        private DkmModuleInstance GetModule(uint id)
        {
            lock (modules)
            {
                return modules[(int)id];
            }
        }

        #region Executing evaluators on correct thread
        /// <summary>
        /// Signal event when there is something new in STA thread queue.
        /// </summary>
        private static System.Threading.AutoResetEvent staThreadActionAvailable = new System.Threading.AutoResetEvent(false);

        /// <summary>
        /// Signal event when STA thread should stop.
        /// </summary>
        private static System.Threading.AutoResetEvent staThreadShouldStop = new System.Threading.AutoResetEvent(false);

        /// <summary>
        /// Queue of STA thread actions.
        /// </summary>
        private static Queue<Action> staThreadActions = new Queue<Action>();

        /// <summary>
        /// Local thread storage for signal event when STA thread process' action.
        /// </summary>
        private static System.Threading.ThreadLocal<System.Threading.AutoResetEvent> threadSyncEvent = new System.Threading.ThreadLocal<System.Threading.AutoResetEvent>(() => new System.Threading.AutoResetEvent(false));

        /// <summary>
        /// STA thread loop function.
        /// </summary>
        private static void StaThreadLoop()
        {
            System.Threading.WaitHandle[] handles = new System.Threading.WaitHandle[] { staThreadShouldStop, staThreadActionAvailable };
            Queue<Action> nextActions = new Queue<Action>();
            bool initializeDkm = initializationForThread.Value;

            while (true)
            {
                // Wait for next operation
                int id = System.Threading.WaitHandle.WaitAny(handles);

                if (handles[id] == staThreadShouldStop)
                    break;

                // Swap actions queue
                lock (staThreadActions)
                {
                    var actions = staThreadActions;
                    staThreadActions = nextActions;
                    nextActions = actions;
                }

                // Execute all actions
                bool initialized = initializationForThread.Value;

                while (nextActions.Count > 0)
                {
                    Action action = nextActions.Dequeue();

                    action();
                }
            }
        }

        /// <summary>
        /// Executes the specified action on STA thread.
        /// </summary>
        /// <param name="action"></param>
        private static void ExecuteOnStaThread(Action action)
        {
            System.Threading.AutoResetEvent syncEvent = threadSyncEvent.Value;
            Exception exception = null;

            lock (staThreadActions)
            {
                staThreadActions.Enqueue(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                    syncEvent.Set();
                });
            }
            staThreadActionAvailable.Set();
            syncEvent.WaitOne();
            if (exception != null)
            {
                throw new AggregateException(exception);
            }
        }

        /// <summary>
        /// Executes the specified evaluator on STA thread.
        /// </summary>
        /// <typeparam name="T">The evaluator result type</typeparam>
        /// <param name="evaluator">The evaluator.</param>
        private static T ExecuteOnStaThread<T>(Func<T> evaluator)
        {
            T result = default(T);

            ExecuteOnStaThread(() =>
            {
                result = evaluator();
            });
            return result;
        }

        /// <summary>
        /// Executes the specified evaluator on DKM initialized thread. It will try to initialize current thread and if it fails it will fall-back to the main thread.
        /// </summary>
        /// <typeparam name="T">The evaluator result type</typeparam>
        /// <param name="evaluator">The evaluator.</param>
        private static void ExecuteOnDkmInitializedThread(Action evaluator)
        {
            if (initializationForThread.Value)
            {
                evaluator();
            }
            else
            {
                ExecuteOnStaThread(evaluator);
            }
        }

        /// <summary>
        /// Executes the specified evaluator on DKM initialized thread. It will try to initialize current thread and if it fails it will fall-back to the main thread.
        /// </summary>
        /// <typeparam name="T">The evaluator result type</typeparam>
        /// <param name="evaluator">The evaluator.</param>
        /// <returns>The evaluator result.</returns>
        private static T ExecuteOnDkmInitializedThread<T>(Func<T> evaluator)
        {
            if (initializationForThread.Value)
            {
                return evaluator();
            }
            else
            {
                return ExecuteOnStaThread(evaluator);
            }
        }
        #endregion
    }
}
