﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.431
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Raven.Studio.Features.Documents.Resources {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class DocumentsResources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal DocumentsResources() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Raven.Studio.Features.Documents.Resources.DocumentsResources", typeof(DocumentsResources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Copy Document Id to Clipboard.
        /// </summary>
        public static string DocumentMenu_CopyId {
            get {
                return ResourceManager.GetString("DocumentMenu_CopyId", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Copy Projection to Clipboard.
        /// </summary>
        public static string DocumentMenu_CopyProjection {
            get {
                return ResourceManager.GetString("DocumentMenu_CopyProjection", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Delete Document.
        /// </summary>
        public static string DocumentMenu_DeleteDocument {
            get {
                return ResourceManager.GetString("DocumentMenu_DeleteDocument", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Delete {0} Documents.
        /// </summary>
        public static string DocumentMenu_DeleteDocuments {
            get {
                return ResourceManager.GetString("DocumentMenu_DeleteDocuments", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Edit Document.
        /// </summary>
        public static string DocumentMenu_EditDocument {
            get {
                return ResourceManager.GetString("DocumentMenu_EditDocument", resourceCulture);
            }
        }
    }
}
