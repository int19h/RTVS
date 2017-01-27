// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.R.DataInspection;

namespace Microsoft.VisualStudio.R.Package.DataInspect.Viewers {
    [Export(typeof(IObjectDetailsViewer))]
    internal sealed class VectorViewer : GridViewerBase {
        private readonly static string[] _excludedClasses = new string[] { "factor" };

        [ImportingConstructor]
        public VectorViewer(IObjectDetailsViewerAggregator aggregator, IDataObjectEvaluator evaluator) :
            base(aggregator, evaluator) { }

        #region IObjectDetailsViewer
        public override bool CanView(IRValueInfo evaluation) {
            return evaluation != null &&
                evaluation.IsAtomic() &&
                evaluation.Length > 1 &&
                (evaluation.Dim == null || evaluation.Dim.Count <= 2) &&
                !evaluation.Classes.Any(t => _excludedClasses.Contains(t));
        }
        #endregion
    }
}
