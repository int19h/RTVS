﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using FluentAssertions;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Shell;
using Microsoft.Languages.Core.Text;
using Microsoft.Languages.Editor.Shell;
using Microsoft.R.Components.ContentTypes;
using Microsoft.R.Editor.Navigation.Peek;
using Microsoft.R.Editor.Test.Mocks;
using Microsoft.R.Host.Client;
using Microsoft.R.Host.Client.Test.Script;
using Microsoft.UnitTests.Core.Mef;
using Microsoft.UnitTests.Core.XUnit;
using Microsoft.VisualStudio.Editor.Mocks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using NSubstitute;
using Xunit;

namespace Microsoft.R.Editor.Test.Navigation {
    [ExcludeFromCodeCoverage]
    [Category.R.Navigation]
    public class RPeekableItemSourceTest : IDisposable {
        private readonly IExportProvider _exportProvider;

        public RPeekableItemSourceTest(REditorMefCatalogFixture catalog) {
            _exportProvider = catalog.CreateExportProvider();
        }

        public void Dispose() {
            _exportProvider.Dispose();
        }

        [Test]
        public void PeekFunction01() {
            string content =
@"
x <- function(a) { }
z <- 1
x()";
            RunUserItemPeekTest(content, 3, 0, "x");
        }

        [Test]
        public void PeekFunction02() {
            string content =
@"
func1 <- function(a) { }
z <- 1
func1()";
            RunUserItemPeekTest(content, 3, 0, "func1", new TextRange(content.Length - 7, 5));
        }

        [Test]
        public void PeekVariable01() {
            string content =
@"
x <- function(a) { }
z <- 1
x()
z";
            RunUserItemPeekTest(content, 4, 0, "z");
        }

        [Test]
        public void PeekArgument01() {
            string content =
@"
x <- function(a) {
    z <- 1
    x()
    a
}";
            RunUserItemPeekTest(content, 4, 4, "a");
        }

        [Test]
        public void PeekInternalFunction01() {
            using (new RHostScript(_exportProvider)) {
                string content = @"lm()";
                RunInternalItemPeekTest(content, 0, 1, "lm");
            }
        }

        private void RunUserItemPeekTest(string content, int line, int column, string name, ITextRange selection = null) {
            var coll = RunPeekTest(content, line, column, name, selection);

            coll[0].DisplayInfo.Title.Should().Be("file.r");
            coll[0].DisplayInfo.Label.Should().Be(name);
            coll[0].DisplayInfo.TitleTooltip.Should().Be(@"C:\file.r");
        }

        private void RunInternalItemPeekTest(string content, int line, int column, string name, ITextRange selection = null) {
            var coll = RunPeekTest(content, line, column, name, selection);

            coll[0].DisplayInfo.Title.Should().Be(name);
            coll[0].DisplayInfo.Label.Should().Be(name);
            coll[0].DisplayInfo.TitleTooltip.Should().Be(name);

            coll[0].DisplayInfo.Should().NotBeNull();

            var dpr = coll[0] as IDocumentPeekResult;
            dpr.Should().NotBeNull();
            dpr.FilePath.Should().NotBeNull();

            Path.GetExtension(dpr.FilePath).Should().Be(".r");
            dpr.FilePath.StartsWithOrdinal(Path.GetTempPath()).Should().BeTrue();
            File.Exists(dpr.FilePath).Should().BeTrue();

            var fi = new FileInfo(dpr.FilePath);
            fi.Length.Should().BeGreaterThan(0);

            var text = File.ReadAllText(dpr.FilePath);
            text.StartsWithIgnoreCase("function(formula, data, subset, weights, na.action, method").Should().BeTrue();
        }

        private IPeekResultCollection RunPeekTest(string content, int line, int column, string name, ITextRange selection = null) {
            List<IPeekableItem> items = new List<IPeekableItem>();

            GetPeekableItems(content, line, column, items, selection);
            items.Should().ContainSingle();
            var item = items[0];

            item.DisplayName.Should().Be(name);
            var source = item.GetOrCreateResultSource(PredefinedPeekRelationships.Definitions.Name);
            source.Should().NotBeNull();

            var coll = new PeekResultCollectionMock();
            var cb = Substitute.For<IFindPeekResultsCallback>();
            source.FindResults(PredefinedPeekRelationships.Definitions.Name, coll, default(CancellationToken), cb);

            coll.Should().HaveCount(1);
            return coll;
        }

        private void GetPeekableItems(string content, int lineNumber, int column, IList<IPeekableItem> items, ITextRange selection = null) {
            var tb = new TextBufferMock(content, RContentTypeDefinition.ContentType);
            var line = tb.CurrentSnapshot.GetLineFromLineNumber(lineNumber);
            GetPeekableItems(content, line.Start + column, items, selection);
        }

        private void GetPeekableItems(string content, int position, IList<IPeekableItem> items, ITextRange selection = null) {
            var document = new EditorDocumentMock(content, @"C:\file.r");

            TextViewMock textView = new TextViewMock(document.TextBuffer, position);

            if (selection != null) {
                textView.Selection.Select(new SnapshotSpan(document.TextBuffer.CurrentSnapshot, new Span(selection.Start, selection.Length)), isReversed: false);
                textView.Caret.MoveTo(new SnapshotPoint(textView.TextBuffer.CurrentSnapshot, selection.End));
            } else {
                textView.Caret.MoveTo(new SnapshotPoint(textView.TextBuffer.CurrentSnapshot, position));
            }

            var peekSession = PeekSessionMock.Create(textView, position);
            var factory = PeekResultFactoryMock.Create();
            var peekSource = new PeekableItemSource(textView.TextBuffer, factory, _exportProvider.GetExportedValue<ICoreShell>());

            peekSource.AugmentPeekSession(peekSession, items);
        }
    }
}
