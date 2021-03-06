﻿//------------------------------------------------------------------------------
// <copyright file="RcStrings.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Caphyon.RcStrings.StringEnhancer;
using Caphyon.RcStrings.VsPackage.Properties;
using EnvDTE;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Caphyon.RcStrings.VsPackage
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
  [PackageRegistration(UseManagedResourcesOnly = true)]
  [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
  [Guid(RcStringsPackage.kPackageGuidString)]
  [ProvideMenuResource("Menus.ctmenu", 1)]
  [ProvideOptionPage(typeof(RcStringsOptionPage), "RC Strings", "RC Strings Page", 0, 0, true)]
  [ProvideAutoLoad(UIContextGuids.SolutionExists)]
  [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
  public sealed class RcStringsPackage : Package
  {

    #region Constants

    private const string kReplaceStringCodeFormated = "{0}";
    private const string kPackageGuidString = "2daea589-e7c3-4b34-ac2d-1d55f7877813";
    private const string kGuidCommands = "6a3a7992-5baf-4e4a-85ee-3947025c5d92";
    private const int kIdSetResourceCmd = 0x0102;
    private const int kIdEditResourceCmd = 0x0101;
    private const int kIdStringResourcesMenuItem = 0x1100;
    private const string kCppProjectKind = "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}";
    private const string kUnloadedProjectKind = "{67294A52-A4F0-11D2-AA88-00C04F688DDE}";
    private string kVs15Version = "2017";
    private Dictionary<string, string> mVsVersions = new Dictionary<string, string>
    {
      {"11.0", "2010"},
      {"12.0", "2012"},
      {"13.0", "2013"},
      {"14.0", "2015"},
      {"15.0", "2017"}
    };
    #endregion

    #region Members

    private bool mReplaceString = true;
    private EnvDTE.DTE mDte;
    private System.Windows.Window mDteWindow;
    private string mSelectedWord = string.Empty;
    private string mReplaceWithCodeFormated = kReplaceStringCodeFormated;
    private RcFile mSelectedRcFile;

    #endregion

    #region Properties

    public UserSettings UserSettings
    {
      get => Settings.Default.UserSettings;
      set => Settings.Default.UserSettings = value;
    }
    private EnvDTE.TextSelection EditorSelection => (EnvDTE.TextSelection)mDte.ActiveDocument.Selection;
    public string SolutionName => Path.GetFileName(mDte.Solution.FileName);

    public bool RandomIdOption
    {
      get
      {
        RcStringsOptionPage optionPage = (RcStringsOptionPage)GetDialogPage(typeof(RcStringsOptionPage));
        return optionPage.RandomId;
      }
    }

    #endregion

    #region Ctor

    /// <summary>
    /// Initializes a new instance of the <see cref="RcStringsPackage"/> class.
    /// </summary>
    public RcStringsPackage()
    {
      // Inside this method you can place any initialization code that does not require
      // any Visual Studio service because at this point the package object is created but
      // not sited yet inside Visual Studio environment. The place to do all the other
      // initialization is the Initialize method.
    }
    #endregion

    #region Package Members

    /// <summary>
    /// Initialization of the package; this method is called right after the package is sited, so this is the place
    /// where you can put all the initialization code that rely on services provided by VisualStudio.
    /// </summary>
    protected override void Initialize()
    {
      base.Initialize();

      mDte = (EnvDTE.DTE)GetService(typeof(EnvDTE.DTE));
      mDteWindow = (System.Windows.Window)HwndSource.FromHwnd((IntPtr)mDte.MainWindow.HWnd).RootVisual;


      mVsVersions.TryGetValue(mDte.Version, out string version);
      if (kVs15Version == version)
      {
        Vs15SolutionLoader solutionLoader = new Vs15SolutionLoader(this);
        solutionLoader.EnsureSolutionProjectsAreLoaded();
      }

      var mSolutionEvent = mDte.Events.SolutionEvents;
      mSolutionEvent.BeforeClosing += SolutionEvent_BeforeClosing;

      var service = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
      if (null != service)
      {
        var menuItem = new MenuCommand(null,
          new CommandID(new Guid(kGuidCommands), kIdStringResourcesMenuItem));
        var setResourceCommand = new OleMenuCommand(SetResourceCommandClick,
           new CommandID(new Guid(kGuidCommands), kIdSetResourceCmd));
        var editResourceCommand = new OleMenuCommand(EditResourceCommandClick,
           new CommandID(new Guid(kGuidCommands), kIdEditResourceCmd));

        editResourceCommand.BeforeQueryStatus += new EventHandler(CommandBeforeQueryStatus);
        setResourceCommand.BeforeQueryStatus += new EventHandler(CommandBeforeQueryStatus);

        service.AddCommand(setResourceCommand);
        service.AddCommand(editResourceCommand);
      }
    }

    private void CommandBeforeQueryStatus(object sender, EventArgs e)
    {
      var command = (OleMenuCommand)sender;
      int id = command.CommandID.ID;
      bool isActiveDocumentInCppProj = mDte.ActiveDocument != null &&
        mDte.ActiveDocument.ProjectItem != null &&
        mDte.ActiveDocument.ProjectItem.ContainingProject != null &&
        mDte.ActiveDocument.ProjectItem.ContainingProject.Kind == kCppProjectKind;

      if (id == kIdEditResourceCmd || id == kIdSetResourceCmd)
      {
        UpdateQueryWord();
        command.Visible = string.Empty != mSelectedWord && isActiveDocumentInCppProj;
      }
    }

    private void UpdateQueryWord()
    {
      if (EditorSelection.IsEmpty)
      {
        // Detect the query word if nothing is selected.
        mSelectedWord = string.Empty;
      }
      else
      {
        // Get the text from the textbox
        // Skip new line
        int retIndex = EditorSelection.Text.IndexOf("\r\n");
        mSelectedWord = retIndex != -1 ?
          EditorSelection.Text.Substring(0, retIndex) :
          EditorSelection.Text.Trim();
      }
    }

    private void SolutionEvent_BeforeClosing()
    {
      if (mSelectedRcFile != null)
      {
        if (UserSettings == null)
          UserSettings = new UserSettings();

        var userSolution = UserSettings.SolutionsSelectedRc.
          FirstOrDefault(s => s.SolutionName == SolutionName);

        if (userSolution == null)
        {
          userSolution = new RcFileInfo();
          UserSettings.SolutionsSelectedRc.Add(userSolution);
        }

        userSolution.SolutionName = SolutionName;
        userSolution.ProjectName = mSelectedRcFile.Project.ProjectName;
        userSolution.SelectedRc = mSelectedRcFile.FileName;
        userSolution.ReplaceWith = mReplaceWithCodeFormated;
        userSolution.IsReplacingWith = mReplaceString;

        Settings.Default.Save();
      }
      mSelectedRcFile = null;
    }

    #region Commands

    private void SetResourceCommandClick(object sender, EventArgs e)
    {
      List<RcFile> rcFiles = GetRCFilesFromSolution();
      IdGenerator.RandomId = RandomIdOption;

      if (UserSettings != null && mSelectedRcFile == null)
      {
        var userSolutionRc = UserSettings.SolutionsSelectedRc
          .FirstOrDefault(src => src.SolutionName == SolutionName);

        if (userSolutionRc != null)
        {
          var currentRcFile = rcFiles.FirstOrDefault(
            rcf => rcf.FileName == userSolutionRc.SelectedRc &&
            rcf.Project.ProjectName == userSolutionRc.ProjectName);
          mSelectedRcFile = currentRcFile;
          mReplaceWithCodeFormated = HandleEmptyReplaceWithField(userSolutionRc.ReplaceWith);
          mReplaceString = userSolutionRc.IsReplacingWith;
        }
      }

      if (rcFiles.Count == 0)
        throw new Exception("No RC files detected");

      EditStringResourceDialog dialog = new EditStringResourceDialog((IServiceProvider)this, rcFiles,
        mSelectedRcFile, mSelectedWord, mReplaceString, mReplaceWithCodeFormated)
      {
        Owner = mDteWindow
      };

      if (dialog.ShowModal() == true)
      {
        try
        {
          // Save added string resource to RC file
          StringResourceContext resourceContext = dialog.ResourceContext;
          resourceContext.AddResource(new EscapeSequences().Format(dialog.ResourceValue),
            dialog.ResourceName, dialog.ResourceId);
          resourceContext.UpdateResourceFiles((IServiceProvider)this);

          // Replace selected text
          if (dialog.ReplaceCode)
            ReplaceSelectedCode(dialog.ReplaceWithCode);
        }
        catch (Exception exception)
        {
          VsShellUtilities.ShowMessageBox((IServiceProvider)this, exception.Message, "Error",
            OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
      }

      // Save current values
      mReplaceString = dialog.ReplaceCode;
      mReplaceWithCodeFormated = HandleEmptyReplaceWithField(dialog.ReplaceStringCodeFormated);
      mSelectedRcFile = dialog.SelectedRcFile;
    }

    private string HandleEmptyReplaceWithField(string replaceString) =>
      String.IsNullOrEmpty(replaceString) ? kReplaceStringCodeFormated : replaceString;

    private void EditResourceCommandClick(object sender, EventArgs e)
    {
      var result = FindStringResourceByName(mSelectedWord);
      if (result == null)
      {
        VsShellUtilities.ShowMessageBox((IServiceProvider)this,
          string.Format("The string resource name \"{0}\" can not be found in RC files in the solution", mSelectedWord),
          "Resource not found", OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        return;
      }

      StringLine stringResource = result.Item1;
      StringResourceContext context = result.Item2;
      IdGenerator.RandomId = RandomIdOption;

      EditStringResourceDialog dialog = new EditStringResourceDialog(
        (IServiceProvider)this, new List<RcFile>() { result.Item2.RcFile },
        result.Item2.RcFile, mSelectedWord,
        mReplaceString, mReplaceWithCodeFormated, stringResource)
      {
        Owner = mDteWindow
      };

      if (dialog.ShowModal() == true)
      {
        try
        {
          // Save added string resource to RC file
          stringResource.Value = new EscapeSequences().Format(dialog.ResourceValue);
          context.UpdateResourceFiles((IServiceProvider)this);
        }
        catch (Exception ex)
        {
          VsShellUtilities.ShowMessageBox((IServiceProvider)this, ex.Message, "Error",
            OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
      }
    }

    /// <summary>
    /// Iterate string resources from entire solution searching the string resource name.
    /// </summary>
    /// <param name="aResourceName"></param>
    /// <returns> 
    ///   The first string resource with the specified name and the 
    ///   context of RC file containing it or null if not found.
    /// </returns>
    private Tuple<StringLine, StringResourceContext> FindStringResourceByName(string aResourceName)
    {
      List<RcFile> rcFiles = GetRCFilesFromSolution();
      foreach (RcFile rcFile in rcFiles)
      {
        StringResourceContext context = new StringResourceContext(rcFile);
        StringLine stringResource = context.GetStringResourceByName(mSelectedWord);
        if (stringResource != null)
          return new Tuple<StringLine, StringResourceContext>(stringResource, context);
      }
      return null;
    }

    #endregion Commands

    #region Editor
    /// <summary>
    /// Replaces the selected code.
    /// </summary>
    /// <param name="aReplaceWithCode">The new code that will replace the selection</param>
    private void ReplaceSelectedCode(string aReplaceWithCode)
    {
      var selectionStartPoint = EditorSelection.AnchorPoint.CreateEditPoint();
      var selectionEndPoint = EditorSelection.ActivePoint.CreateEditPoint();

      selectionStartPoint.ReplaceText(selectionEndPoint, aReplaceWithCode,
        (int)vsEPReplaceTextOptions.vsEPReplaceTextAutoformat);
    }
    #endregion Editor

    #endregion

    #region Get RC files from solution

    /// <summary>
    /// Scan every project in the solution for .rc files.
    /// </summary>
    /// <returns></returns>
    private List<RcFile> GetRCFilesFromSolution()
    {
      List<RcFile> rcFiles = new List<RcFile>();
      List<EnvDTE.Project> solutionProjects = AutomationUtil.GetAllProjects(mDte);
      foreach (EnvDTE.Project project in solutionProjects)
        rcFiles.AddRange(GetRcFilesFromProject(project));
      return rcFiles;
    }

    private List<RcFile> GetRcFilesFromProject(EnvDTE.Project aProject)
    {
      if (string.Compare(EnvDTE.Constants.vsProjectKindUnmodeled, aProject.Kind,
        System.StringComparison.OrdinalIgnoreCase) == 0)
      {
        aProject = AutomationUtil.ReloadProject(this, aProject);
      }

      if (aProject == null || string.Compare(kCppProjectKind, aProject.Kind,
        System.StringComparison.OrdinalIgnoreCase) != 0)
      {
        return new List<RcFile>();
      }

      var rcFiles = GetRcFiles(aProject.ProjectItems, string.Empty);
      if (rcFiles.Count == 0)
        return rcFiles;

      ProjectCollection projectCollection = new ProjectCollection();
      var project = projectCollection.LoadProject(aProject.FullName);
      HashSet<string> aditionalDirectories = GetAdditionalIncludeDirectories(project.Items, aProject);

      VCppProject cppProject = new VCppProject(aProject);
      cppProject.AditionalIncludeDirectories.AddRange(aditionalDirectories);
      rcFiles.ForEach(rcf => rcf.Project = cppProject);

      return rcFiles;
    }

    private HashSet<string> GetAdditionalIncludeDirectories(
      ICollection<Microsoft.Build.Evaluation.ProjectItem> aProjectItems, EnvDTE.Project aProject)
    {
      HashSet<string> additionalDirectories = new HashSet<string>();
      foreach (var item in aProjectItems)
      {
        if (item.ItemType != "ResourceCompile" && item.ItemType != "ClCompile")
          continue;

        var aditionalDirsProp = item.GetMetadata("AdditionalIncludeDirectories");
        if (aditionalDirsProp == null)
          continue;

        string[] aditionalDirs = aditionalDirsProp.EvaluatedValue.Split(';');
        foreach (string dirRelativePath in aditionalDirs)
        {
          try
          {
            string dirFullPath = Path.Combine(Path.GetDirectoryName(aProject.FileName), dirRelativePath);
            string dirAbsolutePath = Path.GetFullPath((new Uri(dirFullPath)).LocalPath);
            if (Directory.Exists(dirAbsolutePath))
              additionalDirectories.Add(dirAbsolutePath);
          }
          catch { }
        }
      }
      return additionalDirectories;
    }

    private List<RcFile> GetRcFiles(EnvDTE.ProjectItems aItems, string aItemPath)
    {
      List<RcFile> outputItems = new List<RcFile>();
      foreach (var item in aItems)
      {
        EnvDTE.ProjectItem projItem = item as EnvDTE.ProjectItem;
        if (projItem == null)
          continue;
        try
        {
          if (projItem.ProjectItems.Count > 0)
          {
            outputItems.AddRange(GetRcFiles(projItem.ProjectItems, Path.Combine(aItemPath, projItem.Name)));
            continue;
          }
          string filePath = projItem.FileCount > 0 ? projItem.FileNames[0] : string.Empty;
          if (Path.GetExtension(filePath).ToLower() == ".rc")
            outputItems.Add(new RcFile(filePath));
        }
        catch { }
      }
      return outputItems;
    }

    #endregion Get RC files from solution
  }
}
