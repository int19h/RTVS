﻿using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.VisualStudio.ProjectSystem.FileSystemMirroring.Shell {
    [Guid("FDFD5BE5-A51B-42D6-932C-CF95686EA4DB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComImport]
    public interface IVsProjectGenerator {
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void RunGenerator([ComAliasName("OLE.LPCWSTR"), MarshalAs(UnmanagedType.LPWStr), In] string szSourceFileMoniker, out bool pfProjectIsGenerated, [MarshalAs(UnmanagedType.BStr)] out string pbstrGeneratedFile, out Guid pGuidProjType);
    }
}
