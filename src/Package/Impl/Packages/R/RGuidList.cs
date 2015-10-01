﻿using System;

namespace Microsoft.VisualStudio.R.Package
{
    public static class RGuidList
    {
        public const string RPackageGuidString = "6D7C5336-C0CA-4857-A7E7-2E964EA836BF";
        public const string REditorFactoryGuidString = "EE606CC0-077A-4FDE-91C3-24EC012C8389";
        public const string RLanguageServiceGuidString = "29C0D8E0-C01C-412B-BEE8-7A7A253A31E6";
        public const string RInteractiveCommandSetGuidString = "E1B81198-F2CF-46EE-BAF1-ACB4AE066A5C";

        public const string RCmdSetGuidString = "AD87578C-B324-44DC-A12A-B01A6ED5C6E3";
        public const string ProjectFileGeneratorGuidString = "62FC63E0-1A66-4B4A-B61A-A6C8BA558FC6";
        public const string CpsProjectFactoryGuidString = "DA7A21FA-8162-4350-AD77-A8D1B671F3ED";

        public const string ReplInteractiveWindowProviderGuidString = "C2582843-58C9-4FE7-B4BD-864C17AD7CE2";
        public const string ReplWindowGuidString = "7026C640-8831-43A4-A93A-A56AA6BB9552";
        public const string PlotWindowGuidString = "FE3D0077-CFD8-4178-A755-9B98D0FB6458";

        public static readonly Guid RPackageGuid = new Guid(RPackageGuidString);
        public static readonly Guid REditorFactoryGuid = new Guid(REditorFactoryGuidString);
        public static readonly Guid RLanguageServiceGuid = new Guid(RLanguageServiceGuidString);
        public static readonly Guid RInteractiveCommandSetGuid = new Guid(RInteractiveCommandSetGuidString);

        public static readonly Guid RCmdSetGuid = new Guid(RCmdSetGuidString);
        public static readonly Guid CpsProjectFactoryGuid = new Guid(CpsProjectFactoryGuidString);

        public static readonly Guid ReplInteractiveWindowProviderGuid = new Guid(ReplInteractiveWindowProviderGuidString);
        public static readonly Guid ReplWindowGuid = new Guid(ReplWindowGuidString);
        public static readonly Guid PlotWindowGuid = new Guid(PlotWindowGuidString);
    };
}