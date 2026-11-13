using com.insanitydesign.MarkdownViewerPlusPlus.Forms;
using com.insanitydesign.MarkdownViewerPlusPlus.Properties;
using Kbg.NppPluginNET;
using Kbg.NppPluginNET.PluginInfrastructure;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static com.insanitydesign.MarkdownViewerPlusPlus.MarkdownViewerConfiguration;
using static Kbg.NppPluginNET.PluginInfrastructure.Win32;

/// <summary>
/// 
/// </summary>
namespace com.insanitydesign.MarkdownViewerPlusPlus
{
    /// <summary>
    /// 
    /// </summary>
    public class MarkdownViewer
    {
        /// <summary>
        /// 
        /// </summary>
        public struct FileInformation
        {
            /// <summary>
            /// 
            /// </summary>
            public string FileDirectory { get; set; }
            /// <summary>
            /// 
            /// </summary>
            private string fileName;
            /// <summary>
            /// 
            /// </summary>
            public string FileName {
                get {
                    return this.fileName;
                }
                set {
                    this.fileName = value;
                    this.FileExtension = Path.GetExtension(this.fileName);
                    this.FileExtension = this.FileExtension.StartsWith(".") ? this.FileExtension.Substring(1, this.FileExtension.Length - 1) : this.FileExtension;
                }
            }
            /// <summary>
            /// 
            /// </summary>
            public string FileExtension { get; private set; }
        }

        /// <summary>
        /// 
        /// </summary>
        public IScintillaGateway Editor { get; protected set; }
        /// <summary>
        /// 
        /// </summary>
        public INotepadPPGateway Notepad { get; protected set; }

        /// <summary>
        /// 
        /// </summary>
        public FileInformation FileInfo { get; protected set; }

        /// <summary>
        /// 
        /// </summary>
        protected AbstractRenderer renderer;

        /// <summary>
        /// 
        /// </summary>
        protected bool updateRenderer = true;

        /// <summary>
        /// 
        /// </summary>
        public int commandId = 0;

        /// <summary>
        /// 
        /// </summary>
        public int commandIdSynchronize = 2;

        /// <summary>
        /// 
        /// </summary>
        public int commandIdOptions = 4;

        /// <summary>
        /// 
        /// </summary>
        public int commandIdAbout = 5;

        /// <summary>
        /// 
        /// </summary>
        protected MarkdownViewerConfiguration configuration;
        
        /// <summary>
        /// 
        /// </summary>
        public Options Options {
            get {
                return this.configuration.options;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public MarkdownViewer()
        {
            //Get some global references to the editor and Notepad++ engines
            this.Editor = new ScintillaGateway(PluginBase.GetCurrentScintilla());
            this.Notepad = new NotepadPPGateway();
            //Init the actual renderer
            this.renderer = new MarkdownViewerRenderer(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="notification"></param>
        public void OnNotification(ScNotification notification)
        {
            // Once Notepad++ has finished starting up, restore the viewer to
            // its persisted state (Bug A: persistence).
            if (notification.Header.Code == (uint)NppMsg.NPPN_READY)
            {
                RestoreViewerVisibility();
                return;
            }

            // Buffer switches and language changes must be handled even while
            // the viewer is hidden, so a Markdown file can auto-enable it
            // (Bug A: auto-enable, upstream #72 behaviour).
            if (notification.Header.Code == (uint)NppMsg.NPPN_BUFFERACTIVATED
                || notification.Header.Code == (uint)NppMsg.NPPN_LANGCHANGED)
            {
                //Update the scintilla handle in all cases to keep track of which instance is active
                UpdateEditorInformation();
                this.Editor.CurrentBufferID = notification.Header.IdFrom;
                if (this.renderer.Visible)
                {
                    Update(true, true);
                }
                else if (IsMarkdownFile())
                {
                    ShowViewer();
                }
                return;
            }

            //Listen to any UI update to get informed about all file changes, chars added/removed etc.
            if (this.renderer.Visible)
            {
                //Check for updates
                if (notification.Header.Code == (uint)SciMsg.SCN_UPDATEUI)
                {
                    //Update the view
                    Update((notification.Updated & (uint)SciMsg.SC_UPDATE_V_SCROLL) != 0);
                }
                else if (notification.Header.Code == (uint)SciMsg.SCN_MODIFIED && !this.updateRenderer)
                {
                    bool isInsert = (notification.ModificationType & (uint)SciMsg.SC_MOD_INSERTTEXT) != 0;
                    bool isDelete = (notification.ModificationType & (uint)SciMsg.SC_MOD_DELETETEXT) != 0;

                    //Track if any text modifications have been made
                    this.updateRenderer = isInsert || isDelete;
                }
            }
        }

        /// <summary>
        /// Restores the viewer after startup: show it again if it was visible
        /// when Notepad++ last saved the options, or if the file active at
        /// startup is a Markdown file (auto-enable). Otherwise leave it hidden.
        /// </summary>
        protected void RestoreViewerVisibility()
        {
            UpdateEditorInformation();
            if (this.configuration.options.rendererVisible || IsMarkdownFile())
            {
                ShowViewer();
            }
        }

        /// <summary>
        /// Shows the viewer and renders the current file.
        /// </summary>
        protected void ShowViewer()
        {
            ToggleToolbarIcon(true);
            UpdateEditorInformation();
            Update(true, true);
            Editor.SetFocus(true);
        }

        /// <summary>
        /// Whether the current file is a Markdown file: its language is
        /// Markdown (covers Language -&gt; Markdown on any extension) or its
        /// extension is a Markdown extension (covers .md files whose language
        /// is not set). Never true for a file the user's extension filter
        /// excludes, since Update() would only show the "not included"
        /// notice for it. Never auto-hides: callers only use this to show.
        /// </summary>
        protected bool IsMarkdownFile()
        {
            if (!this.configuration.ValidateFileExtension(this.FileInfo.FileExtension, this.FileInfo.FileName))
            {
                return false;
            }
            if (this.Notepad.GetCurrentLanguage() == LangType.L_MARKDOWN)
            {
                return true;
            }
            string extension = this.FileInfo.FileExtension;
            if (extension == null)
            {
                return false;
            }
            switch (extension.ToLowerInvariant())
            {
                case "md":
                case "markdown":
                case "mdown":
                case "mkd":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="updateScrollBar"></param>
        /// <param name="updateRenderer"></param>
        public void Update(bool updateScrollBar = false, bool updateRenderer = false)
        {
            //Validate that the current file may be rendered
            if (this.configuration.ValidateFileExtension(this.FileInfo.FileExtension, this.FileInfo.FileName))
            {
                //Update the view
                this.updateRenderer = updateRenderer ? updateRenderer : this.updateRenderer;
                UpdateMarkdownViewer();
                //Update the scroll bar of the Viewer Panel only in case of vertical scrolls
                if (this.configuration.options.synchronizeScrolling && updateScrollBar)
                {
                    UpdateScrollBar();
                }
            }
            else
            {
                this.renderer.Render($@"<p>
Your configuration settings do not include the currently selected file extension.<br />
The rendered file extensions are <b>'{this.configuration.options.fileExtensions}'</b>.<br />
The current file is <i>'{this.FileInfo.FileName}'</i>.
                </p>", this.FileInfo);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        protected void UpdateScrollBar()
        {
            try
            {
                ScrollInfo scrollInfo = this.Editor.GetScrollInfo(ScrollInfoMask.SIF_RANGE | ScrollInfoMask.SIF_TRACKPOS | ScrollInfoMask.SIF_PAGE, ScrollInfoBar.SB_VERT);
                double totalRange = scrollInfo.nMax - scrollInfo.nMin + 1;
                double scrollRatio;

                // Is "Enable scrolling beyond last line" checked?
                if (Editor.GetEndAtLastLine() == false)
                {
                    var actualThumbHeight = totalRange / (totalRange / scrollInfo.nPage - 1);
                    var actualTrackPos = scrollInfo.nTrackPos * actualThumbHeight / scrollInfo.nPage;

                    scrollRatio = Math.Min(1, actualTrackPos / (totalRange - actualThumbHeight));
                }
                else
                {
                    scrollRatio = scrollInfo.nTrackPos / (totalRange - scrollInfo.nPage);
                }

                this.renderer.ScrollByRatioVertically(scrollRatio);
            }
            catch { }            
        }

        /// <summary>
        /// 
        /// </summary>
        public void CommandMenuInit()
        {
            this.configuration = new MarkdownViewerConfiguration();

            //Register our commands
            PluginBase.SetCommand(this.commandId, Main.PluginName, MarkdownViewerCommand, new ShortcutKey(true, false, true, System.Windows.Forms.Keys.M));
            //Separator
            PluginBase.SetCommand(this.commandId + 1, "---", null);
            //Synchronized scrolling
            PluginBase.SetCommand(this.commandIdSynchronize, "Synchronize scrolling (Editor -> Viewer)", SynchronizeScrollingCommand, this.configuration.options.synchronizeScrolling);
            //Separator
            PluginBase.SetCommand(this.commandIdSynchronize + 1, "---", null);
            //Options
            PluginBase.SetCommand(this.commandIdOptions, "Options", OptionsCommand);
            //About
            PluginBase.SetCommand(this.commandIdAbout, "About", AboutCommand);
        }

        /// <summary>
        /// 
        /// </summary>
        public void OptionsCommand()
        {
            using (MarkdownViewerOptions options = new MarkdownViewerOptions(ref this.configuration))
            {
                if (options.ShowDialog(Control.FromHandle(PluginBase.GetCurrentScintilla())) == DialogResult.OK)
                {
                    //Update after something potentially changed in the settings dialog
                    Update(true, true);
                }
            }
        }

        /// <summary>
        /// Show the AboutDialog as modal to the Notepad++ window
        /// </summary>
        public void AboutCommand()
        {
            using (AboutDialog about = new AboutDialog())
            {
                about.ShowDialog(Control.FromHandle(PluginBase.GetCurrentScintilla()));
            }
        }

        /// <summary>
        /// Check/Uncheck the configuration item
        /// </summary>
        protected void SynchronizeScrollingCommand()
        {
            this.configuration.options.synchronizeScrolling = !this.configuration.options.synchronizeScrolling;
            Win32.CheckMenuItem(Win32.GetMenu(PluginBase.nppData._nppHandle), PluginBase._funcItems.Items[this.commandIdSynchronize]._cmdID, Win32.MF_BYCOMMAND | (this.configuration.options.synchronizeScrolling ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
            if (this.configuration.options.synchronizeScrolling)
            {
                UpdateScrollBar();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void SetToolBarIcon()
        {
            toolbarIcons toolbarIcons = new toolbarIcons();
            toolbarIcons.hToolbarBmp = Resources.markdown_16x16_solid.GetHbitmap();
            IntPtr pTbIcons = Marshal.AllocHGlobal(Marshal.SizeOf(toolbarIcons));
            Marshal.StructureToPtr(toolbarIcons, pTbIcons, false);
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_ADDTOOLBARICON, PluginBase._funcItems.Items[this.commandId]._cmdID, pTbIcons);
            Marshal.FreeHGlobal(pTbIcons);
        }

        /// <summary>
        /// 
        /// </summary>
        public void MarkdownViewerCommand()
        {
            bool visibility = !this.renderer.Visible;
            //Show
            if (visibility)
            {
                ShowViewer();
            }
            else
            {
                ToggleToolbarIcon(false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        protected void UpdateEditorInformation()
        {
            this.Editor.SetScintillaHandle(PluginBase.GetCurrentScintilla());
            this.FileInfo = new FileInformation() {
                FileName = this.Notepad.GetCurrentFileName(),
                FileDirectory = this.Notepad.GetCurrentDirectory()
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="show"></param>
        /// <returns></returns>
        public void ToggleToolbarIcon(bool show = true)
        {
            //To show or not to show
            NppMsg msg = show ? NppMsg.NPPM_DMMSHOW : NppMsg.NPPM_DMMHIDE;
            //
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)msg, 0, this.renderer.Handle);
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_SETMENUITEMCHECK, PluginBase._funcItems.Items[this.commandId]._cmdID, show ? 1 : 0);
            //
            // Persist the visibility so the viewer is restored to the same
            // state on the next start (Bug A). Saved immediately, not only on
            // clean shutdown, so a crash does not lose the toggle. Persist the
            // requested state: the old in-memory field read renderer.Visible
            // back here, but that field was never read anywhere.
            if (this.configuration != null)
            {
                this.configuration.options.rendererVisible = show;
                this.configuration.Save();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        protected void UpdateMarkdownViewer()
        {
            try
            {
                if (this.updateRenderer)
                {
                    this.updateRenderer = false;
                    this.renderer.Render(this.Editor.GetText(this.Editor.GetLength() + 1), this.FileInfo);
                }
            }
            catch
            {
                this.renderer.Render("<p>Couldn't render the currently selected file!</p>", this.FileInfo);
            }
        }

        /// <summary>
        /// Save the configuration
        /// </summary>
        public void PluginCleanUp()
        {
            this.configuration.Save();
        }
    }
}
