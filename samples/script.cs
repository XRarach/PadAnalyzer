using System;
using System.Linq;
using CsScripts;
import helper.cs;

Console.Error.WriteLine("This is sample error");
HelpMe("It works!");

writeln("All Modules: {0}", string.Join(", ", Module.All.Select(m => m.Name)));

writeln("Current process exe: {0}", Process.Current.ExecutableName);
writeln("Current thread {0}:{1}", Thread.Current.Id, Thread.Current.SystemId);
writeln("Current call stack {0}", Thread.Current.StackTrace);
writeln("Current source file: {0}:{1}", Thread.Current.StackTrace.CurrentFrame.SourceFileName, Thread.Current.StackTrace.CurrentFrame.SourceFileLine);
writeln("Current function: {0}", Thread.Current.StackTrace.CurrentFrame.FunctionName);
writeln("Callstack:");
writeln(string.Join("\n", Thread.Current.StackTrace.Frames.Select(f => f.FunctionName)));
writeln("Locals: {0}", string.Join(", ", Thread.Current.StackTrace.CurrentFrame.Locals.Select(v => string.Format("{0} ({1})", v.GetName(), v.GetCodeType()))));
writeln("Arguments: {0}", string.Join(", ", Thread.Current.StackTrace.CurrentFrame.Arguments.Select(v => string.Format("{0} ({1})", v.GetName(), v.GetCodeType()))));

dynamic l = Thread.Current.Locals[0];

writeln(l.GetName());
foreach (var field in l.GetFields())
    writeln("  {0} ({1}) = {0}", field.GetName(), field.GetCodeType(), field);
