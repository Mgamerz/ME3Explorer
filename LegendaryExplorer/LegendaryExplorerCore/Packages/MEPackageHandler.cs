﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Gammtek.IO;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Misc;

namespace LegendaryExplorerCore.Packages
{
    public static class MEPackageHandler
    {
        /// <summary>
        /// Global override for shared cache. Set to false to disable usage of the cache and always force loading packages.
        /// </summary>
        public static bool GlobalSharedCacheEnabled = true;

        static readonly ConcurrentDictionary<string, IMEPackage> openPackages = new ConcurrentDictionary<string, IMEPackage>(StringComparer.OrdinalIgnoreCase);
        public static ObservableCollection<IMEPackage> packagesInTools = new ObservableCollection<IMEPackage>();

        // Package loading for UDK 2014/2015
        static Func<string, bool, UDKPackage> UDKConstructorDelegate;
        static Func<Stream, string, UDKPackage> UDKStreamConstructorDelegate;

        // Package loading for ME games
        static Func<string, MEGame, MEPackage> MEConstructorDelegate;
        static Func<Stream, string, MEPackage> MEStreamConstructorDelegate;

        // Header only loaders. Meant for when you just need to get info about a package without caring about the contents.
        //static Func<string, MEPackage> MEConstructorQuickDelegate;
        static Func<Stream, string, MEPackage> MEConstructorQuickStreamDelegate;

        public static void Initialize()
        {
            UDKConstructorDelegate = UDKPackage.RegisterLoader();
            UDKStreamConstructorDelegate = UDKPackage.RegisterStreamLoader();
            MEConstructorDelegate = MEPackage.RegisterLoader();
            MEStreamConstructorDelegate = MEPackage.RegisterStreamLoader();
            //MEConstructorQuickDelegate = MEPackage.RegisterQuickLoader();
            MEConstructorQuickStreamDelegate = MEPackage.RegisterQuickStreamLoader();
        }

        public static IReadOnlyList<string> GetOpenPackages() => openPackages.Select(x => x.Key).ToList();

        /// <summary>
        /// Opens a package from a stream. Ensure the position is correctly set to the start of the package.
        /// </summary>
        /// <param name="inStream"></param>
        /// <param name="associatedFilePath"></param>
        /// <param name="useSharedPackageCache"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public static IMEPackage OpenMEPackageFromStream(Stream inStream, string associatedFilePath = null, bool useSharedPackageCache = false, IPackageUser user = null, bool quickLoad = false)
        {
            IMEPackage package;
            if (associatedFilePath == null || !useSharedPackageCache || !GlobalSharedCacheEnabled || quickLoad)
            {
                package = LoadPackage(inStream, associatedFilePath, false, quickLoad);
            }
            else
            {
                package = openPackages.GetOrAdd(associatedFilePath, fpath =>
                {
                    Debug.WriteLine($"Adding package to package cache (Stream): {associatedFilePath}");
                    return LoadPackage(inStream, associatedFilePath, true);
                });
            }

            if (user != null)
            {
                package.RegisterTool(user);
                addToPackagesInTools(package);
            }
            else
            {
                package.RegisterUse();
            }
            return package;
        }


        /// <summary>
        /// You should only use this if you know what you're doing! This will forcibly add a package to the open packages cache. Only used when package cache is enabled.
        /// </summary>
        public static void ForcePackageIntoCache(IMEPackage package)
        {
            if (GlobalSharedCacheEnabled)
            {
                Debug.WriteLine($@"Forcing package into cache: {package.FilePath}");
                if (package is UnrealPackageFile upf && upf.RefCount < 1)
                {
                    // Package will immediately be dropped on first dispose
                    Debugger.Break();
                }
                var pathToFile = package.FilePath;
                if (File.Exists(pathToFile))
                {
                    pathToFile = Path.GetFullPath(pathToFile); //STANDARDIZE INPUT IF FILE EXISTS (it might be a memory file!)
                }
                openPackages[pathToFile] = package;
            }
            else
            {
                Debug.WriteLine("Global Package Cache is disabled, cannot force packages into cache");
            }
        }


        /// <summary>
        /// Opens an already open package, registering it for use in a tool.
        /// </summary>
        /// <param name="package"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public static IMEPackage OpenMEPackage(IMEPackage package, IPackageUser user = null)
        {
            if (user != null)
            {
                package.RegisterTool(user);
                addToPackagesInTools(package);
            }
            else
            {
                package.RegisterUse();
            }
            return package;
        }

        /// <summary>
        /// Opens a Mass Effect package file. By default, this call will attempt to return an existing open (non-disposed) package at the same path if it is opened twice. Use the forceLoadFromDisk parameter to ignore this behavior.
        /// </summary>
        /// <param name="pathToFile">Path to the file to open</param>
        /// <param name="user">????</param>
        /// <param name="forceLoadFromDisk">If the package being opened should skip the shared package cache and forcibly load from disk. </param>
        /// <returns></returns>
        public static IMEPackage OpenMEPackage(string pathToFile, IPackageUser user = null, bool forceLoadFromDisk = false, bool quickLoad = false, object diskIOSyncLock = null)
        {
            if (File.Exists(pathToFile))
            {
                pathToFile = Path.GetFullPath(pathToFile); //STANDARDIZE INPUT IF FILE EXISTS (it might be a memory file!)
            }

            IMEPackage package;
            if (forceLoadFromDisk || !GlobalSharedCacheEnabled || quickLoad) //Quick loaded packages cannot be cached
            {
                if (quickLoad)
                {
                    // Quickload: Don't read entire file.
                    if (diskIOSyncLock != null)
                    {
                        MemoryStream ms;
                        lock (diskIOSyncLock)
                        {
                            using (FileStream fs = new FileStream(pathToFile, FileMode.Open, FileAccess.Read))
                            {
                                package = LoadPackage(fs, pathToFile, false, quickLoad);
                            }
                        }
                    }
                    else
                    {
                        using (FileStream fs = new FileStream(pathToFile, FileMode.Open, FileAccess.Read))
                        {
                            package = LoadPackage(fs, pathToFile, false, quickLoad);
                        }
                    }

                }
                else
                {
                    // Reading and operating on memory is faster than seeking on disk
                    if (diskIOSyncLock != null)
                    {
                        MemoryStream ms;
                        lock (diskIOSyncLock)
                        {
                            ms = new MemoryStream(File.ReadAllBytes(pathToFile));
                        }
                        var p = LoadPackage(ms, pathToFile, true);
                        ms.Dispose();
                        return p;
                    }
                    else
                    {
                        using var ms = new MemoryStream(File.ReadAllBytes(pathToFile));
                        return LoadPackage(ms, pathToFile, true);
                    }

                }

            }
            else
            {
                package = openPackages.GetOrAdd(pathToFile, fpath =>
                {
                    // Reading and operating on memory is faster than seeking on disk
                    if (diskIOSyncLock != null)
                    {
                        MemoryStream ms;
                        lock (diskIOSyncLock)
                        {
                            ms = new MemoryStream(File.ReadAllBytes(pathToFile));
                        }
                        var p = LoadPackage(ms, pathToFile, true);
                        ms.Dispose();
                        return p;
                    }
                    else
                    {
                        using var ms = new MemoryStream(File.ReadAllBytes(pathToFile));
                        return LoadPackage(ms, pathToFile, true);
                    }
                });
            }



            if (user != null)
            {
                package.RegisterTool(user);
                addToPackagesInTools(package);
            }
            else
            {
                package.RegisterUse();
            }
            return package;
        }

        /// <summary>
        /// Opens a package, but only reads the header. No names, imports or exports are loaded (and an error will be thrown if any are accessed). The package is not decompressed and is not added to the package cache.
        /// </summary>
        /// <param name="targetPath"></param>
        /// <returns></returns>
        public static IMEPackage QuickOpenMEPackage(string pathToFile)
        {
            if (File.Exists(pathToFile))
            {
                pathToFile = Path.GetFullPath(pathToFile); //STANDARDIZE INPUT IF FILE EXISTS (it might be a memory file!)
            }

            using FileStream fs = new FileStream(pathToFile, FileMode.Open, FileAccess.Read);
            return LoadPackage(fs, pathToFile, false, true);
        }

        private static IMEPackage LoadPackage(Stream stream, string filePath = null, bool useSharedCache = false, bool quickLoad = false)
        {
            ushort version = 0;
            ushort licenseVersion = 0;
            bool fullyCompressed = false;

            EndianReader er = new EndianReader(stream);
            if (stream.ReadUInt32() == UnrealPackageFile.packageTagBigEndian) er.Endian = Endian.Big;

            // This is stored as integer by cooker as it is flipped by size word in big endian
            uint versionLicenseePacked = er.ReadUInt32();
            if ((versionLicenseePacked == 0x00020000 || versionLicenseePacked == 0x00010000) && er.Endian == Endian.Little && filePath != null) //can only load fully compressed packages from disk since we won't know what the .us file has
            {
                //block size - this is a fully compressed file. we must decompress it
                //for some reason fully compressed files use a little endian package tag
                var usfile = filePath + ".us";
                if (File.Exists(usfile))
                {
                    fullyCompressed = true;
                }
                else if (File.Exists(filePath + ".UNCOMPRESSED_SIZE"))
                {
                    fullyCompressed = true;
                }
            }

            if (!fullyCompressed)
            {
                version = (ushort)(versionLicenseePacked & 0xFFFF);
                licenseVersion = (ushort)(versionLicenseePacked >> 16);
            }


            IMEPackage pkg;
            if (fullyCompressed ||
                (version == MEPackage.ME3UnrealVersion && (licenseVersion == MEPackage.ME3LicenseeVersion || licenseVersion == MEPackage.ME3Xenon2011DemoLicenseeVersion)) ||
                version == MEPackage.ME3WiiUUnrealVersion && licenseVersion == MEPackage.ME3LicenseeVersion ||
                version == MEPackage.ME2UnrealVersion && licenseVersion == MEPackage.ME2LicenseeVersion || //PC and Xbox share this
                version == MEPackage.ME2PS3UnrealVersion && licenseVersion == MEPackage.ME2PS3LicenseeVersion ||
                version == MEPackage.ME2DemoUnrealVersion && licenseVersion == MEPackage.ME2LicenseeVersion ||
                version == MEPackage.ME1UnrealVersion && licenseVersion == MEPackage.ME1LicenseeVersion ||
                version == MEPackage.ME1PS3UnrealVersion && licenseVersion == MEPackage.ME1PS3LicenseeVersion ||
                version == MEPackage.ME1XboxUnrealVersion && licenseVersion == MEPackage.ME1XboxLicenseeVersion ||

                // LEGENDARY
                version == MEPackage.LE1UnrealVersion && licenseVersion == MEPackage.LE1LicenseeVersion ||
                version == MEPackage.LE2UnrealVersion && licenseVersion == MEPackage.LE2LicenseeVersion ||
                version == MEPackage.LE3UnrealVersion && licenseVersion == MEPackage.LE3LicenseeVersion



                )
            {
                stream.Position -= 8; //reset to start
                pkg = quickLoad ? MEConstructorQuickStreamDelegate(stream, filePath) : MEStreamConstructorDelegate(stream, filePath);
                MemoryAnalyzer.AddTrackedMemoryItem($"MEPackage {Path.GetFileName(filePath)}", new WeakReference(pkg));
            }
            else if (version == UDKPackage.UDKUnrealVersion || version == 867 && licenseVersion == 0)
            {
                //UDK
                stream.Position -= 8; //reset to start
                pkg = UDKStreamConstructorDelegate(stream, filePath);
                MemoryAnalyzer.AddTrackedMemoryItem($"UDKPackage {Path.GetFileName(filePath)}", new WeakReference(pkg));
            }
            else
            {
                throw new FormatException("Not an ME1, ME2, ME3, LE1, LE2, LE3,or UDK (2015) package file.");
            }

            if (useSharedCache)
            {
                pkg.noLongerUsed += Package_noLongerUsed;
            }

            return pkg;
        }

        /// <summary>
        /// Creates and saves a package. A package is not returned as the saving code will add data that must be re-read for a package to be properly used.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="game"></param>
        public static void CreateAndSavePackage(string path, MEGame game)
        {
            switch (game)
            {
                case MEGame.UDK:
                    UDKConstructorDelegate(path, true).Save();
                    break;
                case MEGame.Unknown:
                    throw new ArgumentException("Cannot create a package file for an Unknown game!", nameof(game));
                default:
                    var package = MEConstructorDelegate(path, game);
                    package.setPlatform(MEPackage.GamePlatform.PC); //Platform must be set or saving code will throw exception (cannot save non-PC platforms)
                    package.Save();
                    break;
            }
        }

        private static void Package_noLongerUsed(UnrealPackageFile sender)
        {
            var packagePath = sender.FilePath;
            if (Path.GetFileNameWithoutExtension(packagePath) != "Core") //Keep Core loaded as it is very often referenced
            {
                if (openPackages.TryRemove(packagePath, out IMEPackage _))
                {
                    Debug.WriteLine($"Released from package cache: {packagePath}");
                }
                else
                {
                    Debug.WriteLine($"Failed to remove package from cache: {packagePath}");
                }
            }
        }

        private static void addToPackagesInTools(IMEPackage package)
        {
            if (!packagesInTools.Contains(package))
            {
                packagesInTools.Add(package);
                package.noLongerOpenInTools += Package_noLongerOpenInTools;
            }
        }

        private static void Package_noLongerOpenInTools(UnrealPackageFile sender)
        {
            IMEPackage package = sender as IMEPackage;
            packagesInTools.Remove(package);
            sender.noLongerOpenInTools -= Package_noLongerOpenInTools;

        }

        public static IMEPackage OpenUDKPackage(string pathToFile, IPackageUser user = null, bool forceLoadFromDisk = false)
        {
            IMEPackage pck = OpenMEPackage(pathToFile, user, forceLoadFromDisk);
            if (pck.Game == MEGame.UDK)
            {
                return pck;
            }

            pck.Release(user);
            throw new FormatException("Not a UDK package file.");
        }

        public static IMEPackage OpenME3Package(string pathToFile, IPackageUser user = null, bool forceLoadFromDisk = false)
        {
            IMEPackage pck = OpenMEPackage(pathToFile, user, forceLoadFromDisk);
            if (pck.Game == MEGame.ME3)
            {
                return pck;
            }

            pck.Release(user);
            throw new FormatException("Not an ME3 package file.");
        }

        public static IMEPackage OpenME2Package(string pathToFile, IPackageUser user = null, bool forceLoadFromDisk = false)
        {
            IMEPackage pck = OpenMEPackage(pathToFile, user, forceLoadFromDisk);
            if (pck.Game == MEGame.ME2)
            {
                return pck;
            }

            pck.Release(user);
            throw new FormatException("Not an ME2 package file.");
        }

        public static IMEPackage OpenME1Package(string pathToFile, IPackageUser user = null, bool forceLoadFromDisk = false)
        {
            IMEPackage pck = OpenMEPackage(pathToFile, user, forceLoadFromDisk);
            if (pck.Game == MEGame.ME1)
            {
                return pck;
            }

            pck.Release(user);
            throw new FormatException("Not an ME1 package file.");
        }

        // LEGENDARY EDITION
        public static IMEPackage OpenLE3Package(string pathToFile, IPackageUser user = null, bool forceLoadFromDisk = false)
        {
            IMEPackage pck = OpenMEPackage(pathToFile, user, forceLoadFromDisk);
            if (pck.Game == MEGame.LE3)
            {
                return pck;
            }

            pck.Release(user);
            throw new FormatException("Not an LE3 package file.");
        }

        public static IMEPackage OpenLE2Package(string pathToFile, IPackageUser user = null, bool forceLoadFromDisk = false)
        {
            IMEPackage pck = OpenMEPackage(pathToFile, user, forceLoadFromDisk);
            if (pck.Game == MEGame.LE2)
            {
                return pck;
            }

            pck.Release(user);
            throw new FormatException("Not an LE2 package file.");
        }

        public static IMEPackage OpenLE1Package(string pathToFile, IPackageUser user = null, bool forceLoadFromDisk = false)
        {
            IMEPackage pck = OpenMEPackage(pathToFile, user, forceLoadFromDisk);
            if (pck.Game == MEGame.LE1)
            {
                return pck;
            }

            pck.Release(user);
            throw new FormatException("Not an LE1 package file.");
        }

        public static bool IsPackageInUse(string pathToFile) => openPackages.ContainsKey(Path.GetFullPath(pathToFile));

        public static void PrintOpenPackages()
        {
            Debug.WriteLine("Open Packages:");
            foreach (KeyValuePair<string, IMEPackage> package in openPackages)
            {
                Debug.WriteLine(package.Key);
            }
        }

        //useful for scanning operations, where a common set of packages are going to be referenced repeatedly
        public static DisposableCollection<IMEPackage> OpenMEPackages(IEnumerable<string> filePaths)
        {
            return new DisposableCollection<IMEPackage>(filePaths.Select(filePath => OpenMEPackage(filePath)));
        }
    }

    public class DisposableCollection<T> : List<T>, IDisposable where T : IDisposable
    {
        public DisposableCollection() : base() { }
        public DisposableCollection(IEnumerable<T> collection) : base(collection) { }
        public DisposableCollection(int capacity) : base(capacity) { }

        public void Dispose()
        {
            foreach (T disposable in this)
            {
                disposable?.Dispose();
            }
        }
    }
}