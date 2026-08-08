using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName ="DataBase/Zom")]
public class ZomDataBase : ScriptableObject
{
    public List<ZomPackage> packages;

    private bool WasChecked = false;

    public ZomPackage GetPackageByName(string name)
    {
        foreach (var package in packages)
        {
            if(package.name == name) return package;
        }
        return null;
    }
    public ZomPackage GetPackageByIndex(int index)
    {
        if (index < 0 || index > packages.Count - 1) return null;
        return packages[index];
    }
    public List<ZomPackage> GetPackageList() => packages;
    public bool AllIndexAreReady()
    {
        if (packages.Count == 0) return false;
        if (WasChecked) return true;
        for (int i = packages.Count - 1; i >= 0; i--)
        {
            if (packages[i] == null)
            {
                packages.Remove(packages[i]);
                continue;
            }
        }
        WasChecked = true;
        return true;
    }
    public bool CanUseDataBase() => (packages !=null) && (packages.Count > 0) && (AllIndexAreReady());
}