﻿using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using ActiproSoftware.Windows.Controls.SyntaxEditor.IntelliPrompt;
using ActiproSoftware.Windows.Controls.SyntaxEditor.IntelliPrompt.Implementation;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Outlining.Implementation;
using Raven.Studio.Controls;
using ActiproSoftware.Text;

namespace Raven.Studio.Features.JsonEditor
{
    public class JsonSyntaxLanguageExtended : JsonSyntaxLanguage
    {
        public JsonSyntaxLanguageExtended() : base()
        { 
            this.RegisterOutliner(new TokenOutliner<JsonOutliningSource>());

            this.RegisterParser(new JsonParser());

            // Register a tagger provider on the language as a service that can create CustomTag objects
            this.RegisterService(new TextViewTaggerProvider<WordHighlightTagger>(typeof(WordHighlightTagger)));

            // Register a tagger provider for showing parse errors
            this.RegisterService<ICodeDocumentTaggerProvider>(new CodeDocumentTaggerProvider<ParseErrorTagger>(typeof(ParseErrorTagger)));

            // Register a squiggle tag quick info provider
            this.RegisterService<IQuickInfoProvider>(new SquiggleTagQuickInfoProvider());
        }
    }
}
