﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;

namespace Caphyon.RcStrings.VsPackage
{
  public class VCppProject
  {
    #region Properties

    public string ProjectName { get; private set; }
    public List<string> AditionalIncludeDirectories { get; }
    public Project VsProject { get; private set; }

    #endregion

    #region Ctor

    public VCppProject(Project aProject)
    {
      VsProject = aProject;
      ProjectName = aProject.Name;
      AditionalIncludeDirectories = new List<string>();
    }

    public VCppProject() {}

    #endregion
  }
}