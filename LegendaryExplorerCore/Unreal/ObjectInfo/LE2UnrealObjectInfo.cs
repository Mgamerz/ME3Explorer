﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Gammtek.IO;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using Newtonsoft.Json;

namespace LegendaryExplorerCore.Unreal.ObjectInfo
{
    public static class LE2UnrealObjectInfo
    {
#if AZURE
        /// <summary>
        /// Full path to where mini files are stored (Core.pcc, Engine.pcc, for example) to enable dynamic lookup of property info like struct defaults
        /// </summary>
        public static string MiniGameFilesPath { get; set; }
#endif


        public static Dictionary<string, ClassInfo> Classes = new();
        public static Dictionary<string, ClassInfo> Structs = new();
        public static Dictionary<string, SequenceObjectInfo> SequenceObjects = new();
        public static Dictionary<string, List<NameReference>> Enums = new();

        private static readonly string[] ImmutableStructs = { "Vector", "Color", "LinearColor", "TwoVectors", "Vector4", "Vector2D", "Rotator", "Guid", "Plane", "Box",
            "Quat", "Matrix", "IntPoint", "ActorReference", "ActorReference", "ActorReference", "PolyReference", "AimTransform", "AimTransform", "AimOffsetProfile", "FontCharacter",
            "CoverReference", "CoverInfo", "CoverSlot", "BioRwBox", "BioMask4Property", "RwVector2", "RwVector3", "RwVector4", "RwPlane", "RwQuat", "BioRwBox44" };

        public static bool IsImmutableStruct(string structName)
        {
            return ImmutableStructs.Contains(structName);
        }

        public static bool IsLoaded;
        public static void loadfromJSON()
        {
            if (!IsLoaded)
            {
                try
                {
                    var infoText = ObjectInfoLoader.LoadEmbeddedJSONText(MEGame.LE2);
                    if (infoText != null)
                    {
                        var blob = JsonConvert.DeserializeAnonymousType(infoText, new { SequenceObjects, Classes, Structs, Enums });
                        SequenceObjects = blob.SequenceObjects;
                        Classes = blob.Classes;
                        Structs = blob.Structs;
                        Enums = blob.Enums;
                        AddCustomAndNativeClasses(Classes, SequenceObjects);
                        foreach ((string className, ClassInfo classInfo) in Classes)
                        {
                            classInfo.ClassName = className;
                        }
                        foreach ((string className, ClassInfo classInfo) in Structs)
                        {
                            classInfo.ClassName = className;
                        }
                        IsLoaded = true;
                    }
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        public static SequenceObjectInfo getSequenceObjectInfo(string className)
        {
            return SequenceObjects.TryGetValue(className, out SequenceObjectInfo seqInfo) ? seqInfo : null;
        }

        public static List<string> getSequenceObjectInfoInputLinks(string className)
        {
            if (SequenceObjects.TryGetValue(className, out SequenceObjectInfo seqInfo))
            {
                if (seqInfo.inputLinks != null)
                {
                    return SequenceObjects[className].inputLinks;
                }
                if (Classes.TryGetValue(className, out ClassInfo info) && info.baseClass != "Object" && info.baseClass != "Class")
                {
                    return getSequenceObjectInfoInputLinks(info.baseClass);
                }
            }
            return null;
        }

        public static string getEnumTypefromProp(string className, string propName, ClassInfo nonVanillaClassInfo = null)
        {
            PropertyInfo p = getPropertyInfo(className, propName, nonVanillaClassInfo: nonVanillaClassInfo);
            if (p == null)
            {
                p = getPropertyInfo(className, propName, true, nonVanillaClassInfo);
            }
            return p?.Reference;
        }

        public static List<NameReference> getEnumValues(string enumName, bool includeNone = false)
        {
            if (Enums.ContainsKey(enumName))
            {
                var values = new List<NameReference>(Enums[enumName]);
                if (includeNone)
                {
                    values.Insert(0, "None");
                }
                return values;
            }
            return null;
        }

        public static ArrayType getArrayType(string className, string propName, ExportEntry export = null)
        {
            PropertyInfo p = getPropertyInfo(className, propName, false, containingExport: export);
            if (p == null)
            {
                p = getPropertyInfo(className, propName, true, containingExport: export);
            }
            if (p == null && export != null)
            {
                if (!export.IsClass && export.Class is ExportEntry classExport)
                {
                    export = classExport;
                }
                if (export.IsClass)
                {
                    ClassInfo currentInfo = generateClassInfo(export);
                    currentInfo.baseClass = export.SuperClassName;
                    p = getPropertyInfo(className, propName, false, currentInfo, containingExport: export);
                    if (p == null)
                    {
                        p = getPropertyInfo(className, propName, true, currentInfo, containingExport: export);
                    }
                }
            }
            return getArrayType(p);
        }

#if DEBUG
        public static bool ArrayTypeLookupJustFailed;
#endif

        public static ArrayType getArrayType(PropertyInfo p)
        {
            if (p != null)
            {
                if (p.Reference == "NameProperty")
                {
                    return ArrayType.Name;
                }

                if (Enums.ContainsKey(p.Reference))
                {
                    return ArrayType.Enum;
                }

                if (p.Reference == "BoolProperty")
                {
                    return ArrayType.Bool;
                }

                if (p.Reference == "ByteProperty")
                {
                    return ArrayType.Byte;
                }

                if (p.Reference == "StrProperty")
                {
                    return ArrayType.String;
                }

                if (p.Reference == "FloatProperty")
                {
                    return ArrayType.Float;
                }

                if (p.Reference == "IntProperty")
                {
                    return ArrayType.Int;
                }
                if (p.Reference == "StringRefProperty")
                {
                    return ArrayType.StringRef;
                }

                if (Structs.ContainsKey(p.Reference))
                {
                    return ArrayType.Struct;
                }

                return ArrayType.Object;
            }
#if DEBUG
            ArrayTypeLookupJustFailed = true;
#endif
            Debug.WriteLine("LE2 Array type lookup failed due to no info provided, defaulting to int");
            return LegendaryExplorerCoreLibSettings.Instance.ParseUnknownArrayTypesAsObject ? ArrayType.Object : ArrayType.Int;
        }

        public static PropertyInfo getPropertyInfo(string className, string propName, bool inStruct = false, ClassInfo nonVanillaClassInfo = null, bool reSearch = true, ExportEntry containingExport = null)
        {
            if (className.StartsWith("Default__"))
            {
                className = className.Substring(9);
            }
            Dictionary<string, ClassInfo> temp = inStruct ? Structs : Classes;
            bool infoExists = temp.TryGetValue(className, out ClassInfo info);
            if (!infoExists && nonVanillaClassInfo != null)
            {
                info = nonVanillaClassInfo;
                infoExists = true;
            }
            if (infoExists) //|| (temp = !inStruct ? Structs : Classes).ContainsKey(className))
            {
                //look in class properties
                if (info.properties.TryGetValue(propName, out var propInfo))
                {
                    return propInfo;
                }
                else if (nonVanillaClassInfo != null && nonVanillaClassInfo.properties.TryGetValue(propName, out var nvPropInfo))
                {
                    // This is called if the built info has info the pre-parsed one does. This especially is important for PS3 files 
                    // Cause the LE2 DB isn't 100% accurate for ME1/ME2 specific classes, like biopawn
                    return nvPropInfo;
                }
                //look in structs

                if (inStruct)
                {
                    foreach (PropertyInfo p in info.properties.Values())
                    {
                        if ((p.Type == PropertyType.StructProperty || p.Type == PropertyType.ArrayProperty) && reSearch)
                        {
                            reSearch = false;
                            PropertyInfo val = getPropertyInfo(p.Reference, propName, true, nonVanillaClassInfo, reSearch);
                            if (val != null)
                            {
                                return val;
                            }
                        }
                    }
                }
                //look in base class
                if (temp.ContainsKey(info.baseClass))
                {
                    PropertyInfo val = getPropertyInfo(info.baseClass, propName, inStruct, nonVanillaClassInfo);
                    if (val != null)
                    {
                        return val;
                    }
                }
                else
                {
                    //Baseclass may be modified as well...
                    if (containingExport?.SuperClass is ExportEntry parentExport)
                    {
                        //Class parent is in this file. Generate class parent info and attempt refetch
                        return getPropertyInfo(parentExport.SuperClassName, propName, inStruct, generateClassInfo(parentExport), reSearch: true, parentExport);
                    }
                }
            }

            //if (reSearch)
            //{
            //    PropertyInfo reAttempt = getPropertyInfo(className, propName, !inStruct, nonVanillaClassInfo, reSearch: false);
            //    return reAttempt; //will be null if not found.
            //}
            return null;
        }

        public static PropertyCollection getDefaultStructValue(string structName, bool stripTransients)
        {
            bool isImmutable = IsImmutableStruct(structName);
            if (Structs.TryGetValue(structName, out ClassInfo info))
            {
                try
                {
                    PropertyCollection props = new();
                    while (info != null)
                    {
                        foreach ((string propName, PropertyInfo propInfo) in info.properties)
                        {
                            if (stripTransients && propInfo.Transient)
                            {
                                continue;
                            }
                            if (getDefaultProperty(propName, propInfo, stripTransients, isImmutable) is Property uProp)
                            {
                                props.Add(uProp);
                            }
                        }
                        string filepath = null;
                        if (LE2Directory.GetBioGamePath() != null)
                        {
                            filepath = Path.Combine(LE2Directory.GetBioGamePath(), info.pccPath);
                        }

                        Stream loadStream = null;
                        if (File.Exists(info.pccPath))
                        {
                            filepath = info.pccPath;
                            loadStream = new MemoryStream(File.ReadAllBytes(info.pccPath));
                        }
                        else if (info.pccPath == GlobalUnrealObjectInfo.Me3ExplorerCustomNativeAdditionsName)
                        {
                            filepath = "GAMERESOURCES_LE2";
                            loadStream = LegendaryExplorerCoreUtilities.LoadFileFromCompressedResource("GameResources.zip", LegendaryExplorerCoreLib.CustomResourceFileName(MEGame.LE2));
                        }
                        else if (filepath != null && File.Exists(filepath))
                        {
                            loadStream = new MemoryStream(File.ReadAllBytes(filepath));
                        }
#if AZURE
                        else if (MiniGameFilesPath != null && File.Exists(Path.Combine(MiniGameFilesPath, info.pccPath)))
                        {
                            // Load from test minigame folder. This is only really useful on azure where we don't have access to 
                            // games
                            filepath = Path.Combine(MiniGameFilesPath, info.pccPath);
                            loadStream = new MemoryStream(File.ReadAllBytes(filepath));
                        }
#endif
                        if (loadStream != null)
                        {
                            using (IMEPackage importPCC = MEPackageHandler.OpenMEPackageFromStream(loadStream, filepath, useSharedPackageCache: true))
                            {
                                var exportToRead = importPCC.GetUExport(info.exportIndex);
                                byte[] buff = exportToRead.Data.Skip(0x24).ToArray();
                                PropertyCollection defaults = PropertyCollection.ReadProps(exportToRead, new MemoryStream(buff), structName);
                                foreach (var prop in defaults)
                                {
                                    props.TryReplaceProp(prop);
                                }
                            }
                        }

                        Structs.TryGetValue(info.baseClass, out info);
                    }
                    props.Add(new NoneProperty());

                    return props;
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public static Property getDefaultProperty(string propName, PropertyInfo propInfo, bool stripTransients = true, bool isImmutable = false)
        {
            switch (propInfo.Type)
            {
                case PropertyType.IntProperty:
                    return new IntProperty(0, propName);
                case PropertyType.FloatProperty:
                    return new FloatProperty(0f, propName);
                case PropertyType.DelegateProperty:
                    return new DelegateProperty(0, "None");
                case PropertyType.ObjectProperty:
                    return new ObjectProperty(0, propName);
                case PropertyType.NameProperty:
                    return new NameProperty("None", propName);
                case PropertyType.BoolProperty:
                    return new BoolProperty(false, propName);
                case PropertyType.ByteProperty when propInfo.IsEnumProp():
                    return new EnumProperty(propInfo.Reference, MEGame.LE2, propName);
                case PropertyType.ByteProperty:
                    return new ByteProperty(0, propName);
                case PropertyType.StrProperty:
                    return new StrProperty("", propName);
                case PropertyType.StringRefProperty:
                    return new StringRefProperty(propName);
                case PropertyType.BioMask4Property:
                    return new BioMask4Property(0, propName);
                case PropertyType.ArrayProperty:
                    switch (getArrayType(propInfo))
                    {
                        case ArrayType.Object:
                            return new ArrayProperty<ObjectProperty>(propName);
                        case ArrayType.Name:
                            return new ArrayProperty<NameProperty>(propName);
                        case ArrayType.Enum:
                            return new ArrayProperty<EnumProperty>(propName);
                        case ArrayType.Struct:
                            return new ArrayProperty<StructProperty>(propName);
                        case ArrayType.Bool:
                            return new ArrayProperty<BoolProperty>(propName);
                        case ArrayType.String:
                            return new ArrayProperty<StrProperty>(propName);
                        case ArrayType.Float:
                            return new ArrayProperty<FloatProperty>(propName);
                        case ArrayType.Int:
                            return new ArrayProperty<IntProperty>(propName);
                        case ArrayType.Byte:
                            return new ImmutableByteArrayProperty(propName);
                        default:
                            return null;
                    }
                case PropertyType.StructProperty:
                    isImmutable = isImmutable || GlobalUnrealObjectInfo.IsImmutable(propInfo.Reference, MEGame.LE2);
                    return new StructProperty(propInfo.Reference, getDefaultStructValue(propInfo.Reference, stripTransients), propName, isImmutable);
                case PropertyType.None:
                case PropertyType.Unknown:
                default:
                    return null;
            }
        }

        public static bool InheritsFrom(string className, string baseClass, Dictionary<string, ClassInfo> customClassInfos = null)
        {
            if (baseClass == @"Object") return true; //Everything inherits from Object
            while (true)
            {
                if (className == baseClass)
                {
                    return true;
                }

                if (customClassInfos != null && customClassInfos.ContainsKey(className))
                {
                    className = customClassInfos[className].baseClass;
                }
                else if (Classes.ContainsKey(className))
                {
                    className = Classes[className].baseClass;
                }
                else
                {
                    break;
                }
            }
            return false;
        }

        #region Generating
        //call this method to regenerate LE2ObjectInfo.json
        //Takes a long time (~5 minutes maybe?). Application will be completely unresponsive during that time.
        public static void generateInfo(string outpath)
        {
            var NewClasses = new Dictionary<string, ClassInfo>();
            var NewStructs = new Dictionary<string, ClassInfo>();
            var NewEnums = new Dictionary<string, List<NameReference>>();
            var newSequenceObjects = new Dictionary<string, SequenceObjectInfo>();

            foreach (string filePath in MELoadedFiles.GetOfficialFiles(MEGame.LE2))
            {
                if (Path.GetExtension(filePath) == ".pcc")
                {
                    using IMEPackage pcc = MEPackageHandler.OpenLE2Package(filePath);
                    for (int i = 1; i <= pcc.ExportCount; i++)
                    {
                        ExportEntry exportEntry = pcc.GetUExport(i);
                        string className = exportEntry.ClassName;
                        string objectName = exportEntry.ObjectName.Name;
                        if (className == "Enum")
                        {
                            generateEnumValues(exportEntry, NewEnums);
                        }
                        else if (className == "Class")
                        {
                            if (!NewClasses.ContainsKey(objectName))
                            {
                                NewClasses.Add(objectName, generateClassInfo(exportEntry));
                            }
                            if (GlobalUnrealObjectInfo.IsA(objectName, "SequenceObject", MEGame.LE2))
                            {
                                List<string> inputLinks = generateSequenceObjectInfo(i, pcc);
                                if (!newSequenceObjects.TryGetValue(objectName, out SequenceObjectInfo seqObjInfo))
                                {
                                    seqObjInfo = new SequenceObjectInfo();
                                    newSequenceObjects.Add(objectName, seqObjInfo);
                                }
                                seqObjInfo.inputLinks = inputLinks;
                            }
                        }
                        else if (className == "ScriptStruct")
                        {
                            if (!NewStructs.ContainsKey(objectName))
                            {
                                NewStructs.Add(objectName, generateClassInfo(exportEntry, isStruct: true));
                            }
                        }
                        else if (exportEntry.IsA("SequenceObject"))
                        {
                            if (!newSequenceObjects.TryGetValue(className, out SequenceObjectInfo seqObjInfo))
                            {
                                seqObjInfo = new SequenceObjectInfo();
                                newSequenceObjects.Add(className, seqObjInfo);
                            }

                            int objInstanceVersion = exportEntry.GetProperty<IntProperty>("ObjInstanceVersion");
                            if (objInstanceVersion > seqObjInfo.ObjInstanceVersion)
                            {
                                seqObjInfo.ObjInstanceVersion = objInstanceVersion;
                            }
                        }
                    }
                }
                // System.Diagnostics.Debug.WriteLine($"{i} of {length} processed");
            }

            File.WriteAllText(outpath,
                              JsonConvert.SerializeObject(new { SequenceObjects = newSequenceObjects, Classes = NewClasses, Structs = NewStructs, Enums = NewEnums }, Formatting.Indented));
        }

        private static void AddCustomAndNativeClasses(Dictionary<string, ClassInfo> classes, Dictionary<string, SequenceObjectInfo> sequenceObjects)
        {
            //Custom additions
            //Custom additions are tweaks and additional classes either not automatically able to be determined
            //or new classes designed in the modding scene that must be present in order for parsing to work properly


            // The following is left only as examples if you are building new ones
            /*classes["BioSeqAct_ShowMedals"] = new ClassInfo
            {
                baseClass = "SequenceAction",
                pccPath = GlobalUnrealObjectInfo.Me3ExplorerCustomNativeAdditionsName,
                exportIndex = 22, //in ME3Resources.pcc
                properties =
                {
                    new KeyValuePair<string, PropertyInfo>("bFromMainMenu", new PropertyInfo(PropertyType.BoolProperty)),
                    new KeyValuePair<string, PropertyInfo>("m_oGuiReferenced", new PropertyInfo(PropertyType.ObjectProperty, "GFxMovieInfo"))
                }
            };
            sequenceObjects["BioSeqAct_ShowMedals"] = new SequenceObjectInfo();
            */

        }


        private static List<string> generateSequenceObjectInfo(int i, IMEPackage pcc)
        {
            //+1 to get the Default__ instance
            var inLinks = pcc.GetUExport(i + 1).GetProperty<ArrayProperty<StructProperty>>("InputLinks");
            if (inLinks != null)
            {
                var inputLinks = new List<string>();
                foreach (var seqOpInputLink in inLinks)
                {
                    inputLinks.Add(seqOpInputLink.GetProp<StrProperty>("LinkDesc").Value);
                }
                return inputLinks;
            }

            return null;
        }

        public static ClassInfo generateClassInfo(ExportEntry export, bool isStruct = false)
        {
            IMEPackage pcc = export.FileRef;
            ClassInfo info = new()
            {
                baseClass = export.SuperClassName,
                exportIndex = export.UIndex,
                ClassName = export.ObjectName
            };
            if (export.IsClass)
            {
                UClass classBinary = ObjectBinary.From<UClass>(export);
                info.isAbstract = classBinary.ClassFlags.HasFlag(UnrealFlags.EClassFlags.Abstract);
            }
            if (pcc.FilePath.Contains("BIOGame"))
            {
                info.pccPath = new string(pcc.FilePath.Skip(pcc.FilePath.LastIndexOf("BIOGame") + 8).ToArray());
            }
            else
            {
                info.pccPath = pcc.FilePath; //used for dynamic resolution of files outside the game directory.
            }

            // Is this code correct for console platforms?
            int nextExport = EndianReader.ToInt32(export.Data, isStruct ? 0x14 : 0xC, export.FileRef.Endian);
            while (nextExport > 0)
            {
                var entry = pcc.GetUExport(nextExport);
                //Debug.WriteLine($"GenerateClassInfo parsing child {nextExport} {entry.InstancedFullPath}");
                if (entry.ClassName != "ScriptStruct" && entry.ClassName != "Enum"
                    && entry.ClassName != "Function" && entry.ClassName != "Const" && entry.ClassName != "State")
                {
                    if (!info.properties.ContainsKey(entry.ObjectName.Name))
                    {
                        PropertyInfo p = getProperty(entry);
                        if (p != null)
                        {
                            info.properties.Add(entry.ObjectName.Name, p);
                        }
                    }
                }
                nextExport = EndianReader.ToInt32(entry.Data, export.Game.IsOTGame() ? 0x10 : 0x08, export.FileRef.Endian);
            }
            return info;
        }

        private static void generateEnumValues(ExportEntry export, Dictionary<string, List<NameReference>> NewEnums = null)
        {
            var enumTable = NewEnums ?? Enums;
            string enumName = export.ObjectName.Name;
            if (!enumTable.ContainsKey(enumName))
            {
                var values = new List<NameReference>();
                byte[] buff = export.Data;
                //subtract 1 so that we don't get the MAX value, which is an implementation detail
                int count = BitConverter.ToInt32(buff, 20) - 1;
                for (int i = 0; i < count; i++)
                {
                    int enumValIndex = 24 + i * 8;
                    values.Add(new NameReference(export.FileRef.Names[BitConverter.ToInt32(buff, enumValIndex)], BitConverter.ToInt32(buff, enumValIndex + 4)));
                }
                enumTable.Add(enumName, values);
            }
        }

        private static PropertyInfo getProperty(ExportEntry entry)
        {
            IMEPackage pcc = entry.FileRef;

            string reference = null;
            PropertyType type;
            switch (entry.ClassName)
            {
                case "IntProperty":
                    type = PropertyType.IntProperty;
                    break;
                case "StringRefProperty":
                    type = PropertyType.StringRefProperty;
                    break;
                case "FloatProperty":
                    type = PropertyType.FloatProperty;
                    break;
                case "BoolProperty":
                    type = PropertyType.BoolProperty;
                    break;
                case "StrProperty":
                    type = PropertyType.StrProperty;
                    break;
                case "NameProperty":
                    type = PropertyType.NameProperty;
                    break;
                case "DelegateProperty":
                    type = PropertyType.DelegateProperty;
                    break;
                case "ObjectProperty":
                case "ClassProperty":
                case "ComponentProperty":
                    type = PropertyType.ObjectProperty;
                    reference = pcc.getObjectName(EndianReader.ToInt32(entry.Data, entry.Data.Length - 4, entry.FileRef.Endian));
                    break;
                case "StructProperty":
                    type = PropertyType.StructProperty;
                    reference = pcc.getObjectName(EndianReader.ToInt32(entry.Data, entry.Data.Length - 4, entry.FileRef.Endian));
                    break;
                case "BioMask4Property":
                case "ByteProperty":
                    type = PropertyType.ByteProperty;
                    reference = pcc.getObjectName(EndianReader.ToInt32(entry.Data, entry.Data.Length - 4, entry.FileRef.Endian));
                    break;
                case "ArrayProperty":
                    type = PropertyType.ArrayProperty;
                    // 44 is not correct on other platforms besides PC
                    PropertyInfo arrayTypeProp = getProperty(pcc.GetUExport(EndianReader.ToInt32(entry.Data, entry.FileRef.Platform == MEPackage.GamePlatform.PC ? 44 : 32, entry.FileRef.Endian)));
                    if (arrayTypeProp != null)
                    {
                        switch (arrayTypeProp.Type)
                        {
                            case PropertyType.ObjectProperty:
                            case PropertyType.StructProperty:
                            case PropertyType.ArrayProperty:
                                reference = arrayTypeProp.Reference;
                                break;
                            case PropertyType.ByteProperty:
                                if (arrayTypeProp.Reference == "Class")
                                    reference = arrayTypeProp.Type.ToString();
                                else
                                    reference = arrayTypeProp.Reference;
                                break;
                            case PropertyType.IntProperty:
                            case PropertyType.FloatProperty:
                            case PropertyType.NameProperty:
                            case PropertyType.BoolProperty:
                            case PropertyType.StrProperty:
                            case PropertyType.StringRefProperty:
                            case PropertyType.DelegateProperty:
                                reference = arrayTypeProp.Type.ToString();
                                break;
                            case PropertyType.None:
                            case PropertyType.Unknown:
                            default:
                                Debugger.Break();
                                return null;
                        }
                    }
                    else
                    {
                        return null;
                    }
                    break;
                case "InterfaceProperty":
                default:
                    return null;
            }

            bool transient = ((UnrealFlags.EPropertyFlags)BitConverter.ToUInt64(entry.Data, 24)).HasFlag(UnrealFlags.EPropertyFlags.Transient);
            return new PropertyInfo(type, reference, transient);
        }
        #endregion

        public static bool IsAKnownNativeClass(string className) => NativeClasses.Contains(className);

        /// <summary>
        /// List of all known classes that are only defined in native code. These are not able to be handled for things like InheritsFrom as they are not in the property info database.
        /// </summary>
        public static string[] NativeClasses = new[]
        {
            @"Engine.CodecMovieBink"
        };
    }
}