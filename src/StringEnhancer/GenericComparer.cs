﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Caphyon.RcStrings.StringEnhancer
{
  public class GenericComparer<T> : IComparer<T>
  {
    #region Members
    private readonly Func<T, T, int> mPredicate;
    #endregion

    #region Ctor
    public GenericComparer(Func<T, T, int> aPredicate) => mPredicate = aPredicate;
    #endregion

    #region Public methods
    public int Compare(T x, T y) => mPredicate(x, y);
    #endregion
  }

}