//------------------------------------------------------------------------------
// <copyright file="VSInteractiveWindowPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CsDebugScript.VS
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", productId: "1.4.0", IconResourceID = 400)] // Info on this package for Help/About
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(VSInteractiveWindow))]
    [ProvideService(typeof(IVSUIVisualizerService), ServiceName = nameof(VSUIVisualizerService), IsAsyncQueryable = true)]
    [Guid(VSInteractiveWindowPackage.PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public sealed class VSInteractiveWindowPackage : AsyncPackage
    {
        /// <summary>
        /// VSInteractiveWindowPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "45e073d7-0af8-4c93-8fe8-3c76b4896917";

        /// <summary>
        /// Initializes a new instance of the <see cref="VSInteractiveWindow"/> class.
        /// </summary>
        public VSInteractiveWindowPackage()
        {
            // Inside this method you can place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override System.Threading.Tasks.Task InitializeAsync(System.Threading.CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            VSInteractiveWindowCommand.Initialize(this);
            AddService(typeof(IVSUIVisualizerService), CreateVSUIVisualizerServiceAsync, true);
            return base.InitializeAsync(cancellationToken, progress);
        }

        private System.Threading.Tasks.Task<object> CreateVSUIVisualizerServiceAsync(IAsyncServiceContainer container, System.Threading.CancellationToken cancellationToken, Type serviceType)
        {
            return System.Threading.Tasks.Task.FromResult<object>(new VSUIVisualizerService());
        }

        #endregion
    }
}
