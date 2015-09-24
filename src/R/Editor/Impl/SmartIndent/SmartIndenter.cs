﻿using Microsoft.Languages.Core.Formatting;
using Microsoft.Languages.Editor.Services;
using Microsoft.R.Core.AST;
using Microsoft.R.Core.AST.Scopes.Definitions;
using Microsoft.R.Core.AST.Statements.Definitions;
using Microsoft.R.Core.Formatting;
using Microsoft.R.Editor.Document;
using Microsoft.R.Editor.Document.Definitions;
using Microsoft.R.Editor.Settings;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.R.Editor.SmartIndent
{
    /// <summary>
    /// Provides block and smart indentation in R code
    /// </summary>
    internal sealed class SmartIndenter : ISmartIndent
    {
        private ITextView _textView;
        private ITextBuffer _textBuffer;

        public static SmartIndenter Attach(ITextView textView)
        {
            SmartIndenter indenter = ServiceManager.GetService<SmartIndenter>(textView);

            if (indenter == null)
            {
                indenter = new SmartIndenter(textView);
            }

            return indenter;
        }

        private SmartIndenter(ITextView textView)
        {
            _textView = textView;
            _textBuffer = _textView.TextBuffer;
        }

        #region ISmartIndent;
        public int? GetDesiredIndentation(ITextSnapshotLine line)
        {
            return GetDesiredIndentation(line, REditorSettings.IndentStyle);
        }

        public int? GetDesiredIndentation(ITextSnapshotLine line, IndentStyle indentStyle)
        {
            if (line != null)
            {
                if (indentStyle == IndentStyle.Block)
                {
                    return GetBlockIndent(line);
                }
                else if (indentStyle == IndentStyle.Smart)
                {
                    return GetSmartIndent(line);
                }
            }

            return null;
        }

        public void Dispose()
        {
        }
        #endregion

        public static int InnerIndentSizeFromScope(ITextBuffer textBuffer, IScope scope, RFormatOptions options)
        {
            if (scope != null && scope.OpenCurlyBrace != null)
            {
                ITextSnapshotLine scopeStartLine = textBuffer.CurrentSnapshot.GetLineFromPosition(scope.OpenCurlyBrace.Start);
                return InnerIndentSizeFromLine(scopeStartLine, options);
            }

            return 0;
        }

        public static int OuterIndentSizeFromScope(ITextBuffer textBuffer, IScope scope, RFormatOptions options)
        {
            if (scope != null && scope.OpenCurlyBrace != null)
            {
                ITextSnapshotLine scopeStartLine = textBuffer.CurrentSnapshot.GetLineFromPosition(scope.OpenCurlyBrace.Start);
                return OuterIndentSizeFromLine(scopeStartLine, options);
            }

            return 0;
        }

        public static int InnerIndentSizeFromLine(ITextSnapshotLine line, RFormatOptions options)
        {
            string lineText = line.GetText();
            string leadingWhitespace = lineText.Substring(0, lineText.Length - lineText.TrimStart().Length);
            IndentBuilder indentbuilder = new IndentBuilder(options.IndentType, options.IndentSize, options.TabSize);

            return IndentBuilder.TextIndentInSpaces(leadingWhitespace + indentbuilder.SingleIndentString, options.TabSize);
        }

        public static int OuterIndentSizeFromLine(ITextSnapshotLine line, RFormatOptions options)
        {
            string lineText = line.GetText();
            string leadingWhitespace = lineText.Substring(0, lineText.Length - lineText.TrimStart().Length);

            return IndentBuilder.TextIndentInSpaces(leadingWhitespace, options.TabSize);
        }

        private int? GetBlockIndent(ITextSnapshotLine line)
        {
            int lineNumber = line.LineNumber;

            //Scan the previous lines for the first line that isn't an empty line.
            while (--lineNumber >= 0)
            {
                ITextSnapshotLine previousLine = line.Snapshot.GetLineFromLineNumber(lineNumber);
                if (previousLine.Length > 0)
                {
                    return OuterIndentSizeFromLine(previousLine, REditorSettings.FormatOptions);
                }
            }

            return null;
        }

        private int? GetSmartIndent(ITextSnapshotLine line)
        {
            IREditorDocument document = REditorDocument.TryFromTextBuffer(_textBuffer);
            if(document == null || document.IsTransient)
            {
                return 0;
            }

            AstRoot ast = document.EditorTree.AstRoot;

            // Try conditional without scope first
            if (line.LineNumber > 0)
            {
                ITextSnapshotLine prevLine = line.Snapshot.GetLineFromLineNumber(line.LineNumber - 1);

                string prevLineText = prevLine.GetText();
                int nonWsPosition = prevLine.Start + (prevLineText.Length - prevLineText.TrimStart().Length) + 1;

                IKeywordScopeStatement scopeStatement = ast.GetNodeOfTypeFromPosition<IKeywordScopeStatement>(nonWsPosition);
                if (scopeStatement != null && scopeStatement.Scope == null)
                {
                    return GetBlockIndent(line) + REditorSettings.IndentSize;
                }
            }

            IScope scope = ast.GetNodeOfTypeFromPosition<IScope>(line.Start);
            if (scope != null && scope.OpenCurlyBrace != null)
            {
                return InnerIndentSizeFromScope(_textBuffer, scope, REditorSettings.FormatOptions);
            }

            return 0;
        }
    }
}
