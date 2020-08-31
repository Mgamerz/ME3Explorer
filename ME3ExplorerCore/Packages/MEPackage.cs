﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ME3ExplorerCore.Gammtek.IO;
using ME3ExplorerCore.Gammtek.Paths;
using ME3ExplorerCore.Helpers;
using ME3ExplorerCore.MEDirectories;
using ME3ExplorerCore.Packages.CloningImportingAndRelinking;
using ME3ExplorerCore.Unreal;
using ME3ExplorerCore.Unreal.BinaryConverters;
using ME3ExplorerCore.Unreal.Classes;
using static ME3ExplorerCore.Unreal.UnrealFlags;
using TalkFile = ME3ExplorerCore.ME1.Unreal.Classes.TalkFile;

namespace ME3ExplorerCore.Packages
{
    [Flags]
    public enum PackageChange
    {
        Export = 0x1,
        Import = 0x2,
        Name = 0x4,
        Add = 0x8,
        Remove = 0x10,
        Data = 0x20,
        Header = 0x40,
        Entry = 0x80,
        EntryAdd = Entry | Add,
        EntryRemove = Entry | Remove,
        EntryHeader = Entry | Header,
        ExportData = Export | Data | Entry,
        ExportHeader = Export | EntryHeader,
        ImportHeader = Import | EntryHeader,
        ExportAdd = Export | EntryAdd,
        ImportAdd = Import | EntryAdd,
        ExportRemove = Export | EntryRemove,
        ImportRemove = Import | EntryRemove,
        NameAdd = Name | Add,
        NameRemove = Name | Remove,
        NameEdit = Name | Data
    }

    [DebuggerDisplay("PackageUpdate | {Change} on index {Index}")]
    public readonly struct PackageUpdate
    {
        /// <summary>
        /// Details on what piece of data has changed
        /// </summary>
        public readonly PackageChange Change;
        /// <summary>
        /// index of what item has changed. Meaning depends on value of Change
        /// </summary>
        public readonly int Index;

        public PackageUpdate(PackageChange change, int index)
        {
            this.Change = change;
            this.Index = index;
        }
    }

    public sealed class MEPackage : UnrealPackageFile, IMEPackage, IDisposable
    {
        public const ushort ME1UnrealVersion = 491;
        public const ushort ME1LicenseeVersion = 1008;
        public const ushort ME1XboxUnrealVersion = 391;
        public const ushort ME1XboxLicenseeVersion = 92;
        public const ushort ME1PS3UnrealVersion = 684; //same as ME3 ;)
        public const ushort ME1PS3LicenseeVersion = 153;

        public const ushort ME2UnrealVersion = 512;
        public const ushort ME2PS3UnrealVersion = 684; //Same as ME3 ;)
        public const ushort ME2DemoUnrealVersion = 513;
        public const ushort ME2LicenseeVersion = 130;
        public const ushort ME2PS3LicenseeVersion = 150;

        public const ushort ME3UnrealVersion = 684;
        public const ushort ME3WiiUUnrealVersion = 845;
        public const ushort ME3Xenon2011DemoLicenseeVersion = 185;
        public const ushort ME3LicenseeVersion = 194;

        /// <summary>
        /// Indicates what type of package file this is. 0 is normal, 1 is TESTPATCH patch package.
        /// </summary>
        public int PackageTypeId { get; }

        /// <summary>
        /// This is not useful for modding but we should not be changing the format of the package file.
        /// </summary>
        public List<string> AdditionalPackagesToCook = new List<string>();


        public Endian Endian { get; }
        public MEGame Game { get; private set; } //can only be ME1, ME2, or ME3. UDK is a separate class
        public GamePlatform Platform { get; }

        public enum GamePlatform
        {
            PC,
            Xenon,
            PS3,
            WiiU
        }

        public MELocalization Localization { get; } = MELocalization.None;

        public byte[] getHeader()
        {
            var ms = new MemoryStream();
            WriteHeader(ms);
            return ms.ToArray();
        }

        #region HeaderMisc
        private int Gen0ExportCount;
        private int Gen0NameCount;
        private int Gen0NetworkedObjectCount;
        private int ImportExportGuidsOffset;
        //private int ImportGuidsCount;
        //private int ExportGuidsCount;
        //private int ThumbnailTableOffset;
        private uint packageSource;
        private int unknown4;
        private int unknown6;
        #endregion

        static bool isLoaderRegistered;
        static bool isStreamLoaderRegistered;

        public static Func<string, MEGame, MEPackage> RegisterLoader()
        {
            if (isLoaderRegistered)
            {
                throw new Exception(nameof(MEPackage) + " can only be initialized once");
            }

            isLoaderRegistered = true;
            return (f, g) =>
            {
                if (g != MEGame.Unknown)
                {
                    return new MEPackage(g, f);
                }
                return new MEPackage(new MemoryStream(File.ReadAllBytes(f)), f);
            };
        }

        public static Func<Stream, string, MEPackage> RegisterStreamLoader()
        {
            if (isStreamLoaderRegistered)
            {
                throw new Exception(nameof(MEPackage) + "streamloader can only be initialized once");
            }

            isStreamLoaderRegistered = true;
            return (s, associatedFilePath) => new MEPackage(s, associatedFilePath);
        }


        private MEPackage(MEGame game, string filePath = null) : base(filePath != null ? Path.GetFullPath(filePath) : null)
        {
            //new Package
            Game = game;
            //reasonable defaults?
            Flags = EPackageFlags.Cooked | EPackageFlags.AllowDownload | EPackageFlags.DisallowLazyLoading | EPackageFlags.RequireImportsAlreadyLoaded;
            return;
        }

        /// <summary>
        /// Opens an ME package from the stream. If this file is from a disk, the filePath should be set to support saving and other lookups.
        /// </summary>
        /// <param name="fs"></param>
        /// <param name="filePath"></param>
        private MEPackage(Stream fs, string filePath = null) : base(filePath != null ? Path.GetFullPath(filePath) : null)
        {
            //MemoryStream fs = new MemoryStream(File.ReadAllBytes(filePath));

            #region Header

            //uint magic = fs.ReadUInt32();
            //if (magic != packageTagLittleEndian && magic != packageTagBigEndian)
            //{
            //    throw new FormatException("Not a supported unreal package!");
            //}

            EndianReader packageReader = EndianReader.SetupForPackageReading(fs);
            packageReader.SkipInt32(); //skip magic as we have already read it
            Endian = packageReader.Endian;

            //Big endian means it will be console version and package header is slightly tweaked as some flags are always set

            // This is stored as integer by cooker as it is flipped by size word in big endian
            var versionLicenseePacked = packageReader.ReadUInt32();

            int uncompressedSizeForFullCompressedPackage = 0;
            if ((versionLicenseePacked == 0x00020000 || versionLicenseePacked == 0x00010000) && Endian == Endian.Little)
            {
                //block size - this is a fully compressed file. we must decompress it
                // these files are little endian package tag for some reason
                //var usfile = filePath + ".us";
                //var ucsfile = filePath + ".UNCOMPRESSED_SIZE";
                //if (File.Exists(usfile) || File.Exists(ucsfile))
                //{
                //packageReader.Position = 0xC;
                //var uncompSize = packageReader.ReadInt32();
                ////calculate number of chunks
                //int chunkCoumt = (uncompSize % 0x00020000 == 0)
                //    ?
                //    uncompSize / 0x00020000
                //    :
                //    uncompSize / 0x00020000 + 1; //round up

                //fs = CompressionHelper.DecompressUDK(packageReader, 0x10, CompressionType.LZX, chunkCoumt);
                if (versionLicenseePacked == 0x20000)
                {
                    //Xbox? LZX
                    fs = CompressionHelper.DecompressFullyCompressedPackage(packageReader, CompressionType.LZX);
                }
                else if (versionLicenseePacked == 0x10000)
                {
                    //PS3? LZMA
                    fs = CompressionHelper.DecompressFullyCompressedPackage(packageReader, CompressionType.LZMA);
                } else
                {
                    // ??????
                }
                packageReader = EndianReader.SetupForPackageReading(fs);
                packageReader.SkipInt32(); //skip magic as we have already read it
                Endian = packageReader.Endian;
                versionLicenseePacked = packageReader.ReadUInt32();
                //}
            }

            var unrealVersion = (ushort)(versionLicenseePacked & 0xFFFF);
            var licenseeVersion = (ushort)(versionLicenseePacked >> 16);
            switch (unrealVersion)
            {
                case ME1UnrealVersion when licenseeVersion == ME1LicenseeVersion:
                    Game = MEGame.ME1;
                    Platform = GamePlatform.PC;
                    break;
                case ME1XboxUnrealVersion when licenseeVersion == ME1XboxLicenseeVersion:
                    Game = MEGame.ME1;
                    Platform = GamePlatform.Xenon;
                    break;
                case ME1PS3UnrealVersion when licenseeVersion == ME1PS3LicenseeVersion:
                    Game = MEGame.ME1;
                    Platform = GamePlatform.PS3;
                    break;
                case ME2UnrealVersion when licenseeVersion == ME2LicenseeVersion:
                case ME2DemoUnrealVersion when licenseeVersion == ME2LicenseeVersion:
                    Game = MEGame.ME2;
                    Platform = GamePlatform.PC;
                    break;
                case ME2PS3UnrealVersion when licenseeVersion == ME2PS3LicenseeVersion:
                    Game = MEGame.ME2;
                    Platform = GamePlatform.PS3;
                    break;
                case ME3WiiUUnrealVersion when licenseeVersion == ME3LicenseeVersion:
                    Game = MEGame.ME3;
                    Platform = GamePlatform.WiiU;
                    break;
                case ME3UnrealVersion when licenseeVersion == ME3LicenseeVersion:
                    Game = MEGame.ME3;
                    Platform = GamePlatform.PC;
                    break;
                case ME3UnrealVersion when licenseeVersion == ME3Xenon2011DemoLicenseeVersion:
                    Game = MEGame.ME3;
                    Platform = GamePlatform.Xenon;
                    break;
                default:
                    throw new FormatException("Not a Mass Effect Package!");
            }
            FullHeaderSize = packageReader.ReadInt32();
            int foldernameStrLen = packageReader.ReadInt32();
            //always "None", so don't bother saving result
            if (foldernameStrLen > 0)
                fs.ReadStringASCIINull(foldernameStrLen);
            else
                fs.ReadStringUnicodeNull(foldernameStrLen * -2);

            Flags = (EPackageFlags)packageReader.ReadUInt32();

            //Xenon Demo ME3 doesn't read this
            if (Game == MEGame.ME3 && (Flags.HasFlag(EPackageFlags.Cooked) || Platform != GamePlatform.PC) && Platform != GamePlatform.Xenon)
            {
                //Consoles are always cooked.
                PackageTypeId = packageReader.ReadInt32(); //0 = standard, 1 = patch ? Not entirely sure. patch_001 files with byte = 0 => game does not load

            }

            //if (Platform != GamePlatform.PC)
            //{
            //    NameOffset = packageReader.ReadInt32();
            //    NameCount = packageReader.ReadInt32();
            //    ExportOffset = packageReader.ReadInt32();
            //    ExportCount = packageReader.ReadInt32();
            //    ImportOffset = packageReader.ReadInt32();
            //    ImportCount = packageReader.ReadInt32();
            //}
            //else
            //{
            NameCount = packageReader.ReadInt32();
            NameOffset = packageReader.ReadInt32();
            ExportCount = packageReader.ReadInt32();
            ExportOffset = packageReader.ReadInt32();
            ImportCount = packageReader.ReadInt32();
            ImportOffset = packageReader.ReadInt32();
            //}

            if (Game != MEGame.ME1 || Platform != GamePlatform.Xenon)
            {
                // Seems this doesn't exist on ME1 Xbox
                DependencyTableOffset = packageReader.ReadInt32();
            }

            if (Game == MEGame.ME3 || Platform == GamePlatform.PS3)
            {
                ImportExportGuidsOffset = packageReader.ReadInt32();
                packageReader.ReadInt32(); //ImportGuidsCount always 0
                packageReader.ReadInt32(); //ExportGuidsCount always 0
                packageReader.ReadInt32(); //ThumbnailTableOffset always 0
            }

            PackageGuid = packageReader.ReadGuid();
            uint generationsTableCount = packageReader.ReadUInt32();
            if (generationsTableCount > 0)
            {
                generationsTableCount--;
                Gen0ExportCount = packageReader.ReadInt32();
                Gen0NameCount = packageReader.ReadInt32();
                Gen0NetworkedObjectCount = packageReader.ReadInt32();
            }
            //should never be more than 1 generation, but just in case
            packageReader.Skip(generationsTableCount * 12);

            packageReader.SkipInt32();//engineVersion          Like unrealVersion and licenseeVersion, these 2 are determined by what game this is,
            packageReader.SkipInt32();//cookedContentVersion   so we don't have to read them in

            if ((Game == MEGame.ME2 || Game == MEGame.ME1) && Platform != GamePlatform.PS3) //PS3 on ME3 engine
            {
                packageReader.SkipInt32(); //always 0
                packageReader.SkipInt32(); //always 47699
                unknown4 = packageReader.ReadInt32();
                packageReader.SkipInt32(); //always 1 in ME1, always 1966080 in ME2
            }

            unknown6 = packageReader.ReadInt32();
            var constantVal = packageReader.ReadInt32();//always -1 in ME1 and ME2, always 145358848 in ME3

            if (Game == MEGame.ME1 && Platform != GamePlatform.PS3)
            {
                packageReader.SkipInt32(); //always -1
            }

            //COMPRESSION AND COMPRESSION CHUNKS
            var compressionFlagPosition = packageReader.Position;
            var compressionType = (UnrealPackageFile.CompressionType)packageReader.ReadInt32();
            int numChunks = packageReader.ReadInt32();

            //read package source
            var savedPos = packageReader.Position;
            packageReader.Skip(numChunks * 16); //skip chunk table so we can find package tag


            packageSource = packageReader.ReadUInt32(); //this needs to be read in so it can be properly written back out.

            if ((Game == MEGame.ME2 || Game == MEGame.ME1) && Platform != GamePlatform.PS3)
            {
                packageReader.SkipInt32(); //always 0
            }

            //Doesn't need to be written out, so it doesn't need to be read in
            //keep this here in case one day we learn that this has a purpose
            //Narrator: On Jan 26, 2020 it turns out this was actually necessary to make it work
            //with ME3Tweaks Mixins as old code did not remove this section
            //Also we should strive to ensure closeness to the original source files as possible
            //because debugging things is a huge PITA if you start to remove stuff
            if (Game == MEGame.ME2 || Game == MEGame.ME3 || Platform == GamePlatform.PS3)
            {
                int additionalPackagesToCookCount = packageReader.ReadInt32();
                //var additionalPackagesToCook = new string[additionalPackagesToCookCount];
                for (int i = 0; i < additionalPackagesToCookCount; i++)
                {
                    var packageStr = packageReader.ReadUnrealString();
                    AdditionalPackagesToCook.Add(packageStr);
                }
            }

            packageReader.Position = savedPos; //restore position to chunk table
            Stream inStream = fs;
            if (IsCompressed && numChunks > 0)
            {
                inStream = CompressionHelper.DecompressUDK(packageReader, compressionFlagPosition, game: Game, platform: Platform);
            }


            #endregion

            //if (IsCompressed && numChunks > 0)
            //{
            //    inStream = Game == MEGame.ME3 ? CompressionHelper.DecompressME3(packageReader) : CompressionHelper.DecompressME1orME2(fs);
            //}

            var endian = packageReader.Endian;
            packageReader = new EndianReader(inStream) { Endian = endian };
            //read namelist
            inStream.JumpTo(NameOffset);
            for (int i = 0; i < NameCount; i++)
            {
                names.Add(packageReader.ReadUnrealString());
                if (Game == MEGame.ME1 && Platform != GamePlatform.PS3)
                    inStream.Skip(8);
                else if (Game == MEGame.ME2 && Platform != GamePlatform.PS3)
                    inStream.Skip(4);
            }

            //read importTable
            inStream.JumpTo(ImportOffset);
            for (int i = 0; i < ImportCount; i++)
            {
                ImportEntry imp = new ImportEntry(this, packageReader) { Index = i };
                imp.PropertyChanged += importChanged;
                imports.Add(imp);
            }

            //read exportTable (ExportEntry constructor reads export data)
            inStream.JumpTo(ExportOffset);
            for (int i = 0; i < ExportCount; i++)
            {
                ExportEntry e = new ExportEntry(this, packageReader) { Index = i };
                e.PropertyChanged += exportChanged;
                exports.Add(e);
            }

            if (Game == MEGame.ME1 && Platform == GamePlatform.PC)
            {
                ReadLocalTLKs();
            }


            if (filePath != null)
            {

                string localizationName = Path.GetFileNameWithoutExtension(filePath).ToUpper();
                if (localizationName.Length > 8)
                    localizationName = localizationName.Substring(localizationName.Length - 8, 8);
                switch (localizationName)
                {
                    case "_LOC_DEU":
                        Localization = MELocalization.DEU;
                        break;
                    case "_LOC_ESN":
                        Localization = MELocalization.ESN;
                        break;
                    case "_LOC_FRA":
                        Localization = MELocalization.FRA;
                        break;
                    case "_LOC_INT":
                        Localization = MELocalization.INT;
                        break;
                    case "_LOC_ITA":
                        Localization = MELocalization.ITA;
                        break;
                    case "_LOC_JPN":
                        Localization = MELocalization.JPN;
                        break;
                    case "_LOC_POL":
                        Localization = MELocalization.POL;
                        break;
                    case "_LOC_RUS":
                        Localization = MELocalization.RUS;
                        break;
                    default:
                        Localization = MELocalization.None;
                        break;
                }
            }
        }

        public static Action<MEPackage, string, bool> RegisterSaver() => saveByReconstructing;

        private static void saveByReconstructing(MEPackage mePackage, string path, bool isSaveAs)
        {
            bool compressed = mePackage.IsCompressed;
            mePackage.Flags &= ~EPackageFlags.Compressed;
            try
            {
                var ms = new MemoryStream();

                //just for positioning. We write over this later when the header values have been updated
                mePackage.WriteHeader(ms);

                //name table
                mePackage.NameOffset = (int)ms.Position;
                mePackage.NameCount = mePackage.Gen0NameCount = mePackage.names.Count;
                foreach (string name in mePackage.names)
                {
                    switch (mePackage.Game)
                    {
                        case MEGame.ME1:
                            ms.WriteUnrealStringASCII(name);
                            ms.WriteInt32(0);
                            ms.WriteInt32(458768);
                            break;
                        case MEGame.ME2:
                            ms.WriteUnrealStringASCII(name);
                            ms.WriteInt32(-14);
                            break;
                        case MEGame.ME3:
                            ms.WriteUnrealStringUnicode(name);
                            break;
                    }
                }

                //import table
                mePackage.ImportOffset = (int)ms.Position;
                mePackage.ImportCount = mePackage.imports.Count;
                foreach (ImportEntry e in mePackage.imports)
                {
                    ms.WriteFromBuffer(e.Header);
                }

                //export table
                mePackage.ExportOffset = (int)ms.Position;
                mePackage.ExportCount = mePackage.Gen0ExportCount = mePackage.exports.Count;
                foreach (ExportEntry e in mePackage.exports)
                {
                    e.HeaderOffset = (uint)ms.Position;
                    ms.WriteFromBuffer(e.Header);
                }

                mePackage.DependencyTableOffset = (int)ms.Position;
                ms.WriteInt32(0);//zero-count DependencyTable
                mePackage.FullHeaderSize = mePackage.ImportExportGuidsOffset = (int)ms.Position;

                //export data
                foreach (ExportEntry e in mePackage.exports)
                {
                    //update offsets
                    ObjectBinary objBin = null;
                    if (!e.IsDefaultObject)
                    {
                        switch (e.ClassName)
                        {
                            case "WwiseBank":
                            case "WwiseStream" when e.GetProperty<NameProperty>("Filename") == null:
                            case "TextureMovie" when e.GetProperty<NameProperty>("TextureFileCacheName") == null:
                                objBin = ObjectBinary.From(e);
                                break;
                            case "ShaderCache":
                                UpdateShaderCacheOffsets(e, (int)ms.Position);
                                break;
                        }
                    }
                    e.DataOffset = (int)ms.Position;
                    if (objBin != null)
                    {
                        e.SetBinaryData(objBin);
                    }



                    ms.WriteFromBuffer(e.Data);
                    //update size and offset in already-written header
                    long pos = ms.Position;
                    ms.JumpTo(e.HeaderOffset + 32);
                    ms.WriteInt32(e.DataSize); //DataSize might have been changed by UpdateOffsets
                    ms.WriteInt32(e.DataOffset);
                    ms.JumpTo(pos);
                }

                //re-write header with updated values
                ms.JumpTo(0);
                mePackage.WriteHeader(ms);


                File.WriteAllBytes(path, ms.ToArray());
                if (!isSaveAs)
                {
                    mePackage.AfterSave();
                }
            }
            finally
            {
                //If we're doing save as, reset compressed flag to reflect file on disk
                if (isSaveAs && compressed)
                {
                    mePackage.Flags |= EPackageFlags.Compressed;
                }
            }
        }

        private static void UpdateShaderCacheOffsets(ExportEntry export, int newDataOffset)
        {
            int oldDataOffset = export.DataOffset;
            switch (export.Game)
            {
                case MEGame.ME2:
                {
                    MemoryStream binData = new MemoryStream(export.Data);
                    binData.Seek(export.propsEnd() + 1, SeekOrigin.Begin);

                    int nameList1Count = binData.ReadInt32();
                    binData.Seek(nameList1Count * 12, SeekOrigin.Current);

                    int shaderCount = binData.ReadInt32();
                    for (int i = 0; i < shaderCount; i++)
                    {
                        binData.Seek(24, SeekOrigin.Current);
                        int nextShaderOffset = binData.ReadInt32() - oldDataOffset;
                        binData.Seek(-4, SeekOrigin.Current);
                        binData.WriteInt32(nextShaderOffset + newDataOffset);
                        binData.Seek(nextShaderOffset, SeekOrigin.Begin);
                    }

                    int vertexFactoryMapCount = binData.ReadInt32();
                    binData.Seek(vertexFactoryMapCount * 12, SeekOrigin.Current);

                    int materialShaderMapCount = binData.ReadInt32();
                    for (int i = 0; i < materialShaderMapCount; i++)
                    {
                        binData.Seek(16, SeekOrigin.Current);

                        int switchParamCount = binData.ReadInt32();
                        binData.Seek(switchParamCount * 32, SeekOrigin.Current);

                        int componentMaskParamCount = binData.ReadInt32();
                        binData.Seek(componentMaskParamCount * 44, SeekOrigin.Current);

                        int nextMaterialShaderMapOffset = binData.ReadInt32() - oldDataOffset;
                        binData.Seek(-4, SeekOrigin.Current);
                        binData.WriteInt32(nextMaterialShaderMapOffset + newDataOffset);
                        binData.Seek(nextMaterialShaderMapOffset, SeekOrigin.Begin);
                    }

                    export.Data = binData.ToArray();
                    break;
                }
                case MEGame.ME3:
                {
                    MemoryStream binData = new MemoryStream(export.Data);
                    binData.Seek(export.propsEnd() + 1, SeekOrigin.Begin);

                    int nameList1Count = binData.ReadInt32();
                    binData.Seek(nameList1Count * 12, SeekOrigin.Current);

                    int namelist2Count = binData.ReadInt32();//namelist2
                    binData.Seek(namelist2Count * 12, SeekOrigin.Current);

                    int shaderCount = binData.ReadInt32();
                    for (int i = 0; i < shaderCount; i++)
                    {
                        binData.Seek(24, SeekOrigin.Current);
                        int nextShaderOffset = binData.ReadInt32() - oldDataOffset;
                        binData.Seek(-4, SeekOrigin.Current);
                        binData.WriteInt32(nextShaderOffset + newDataOffset);
                        binData.Seek(nextShaderOffset, SeekOrigin.Begin);
                    }

                    int vertexFactoryMapCount = binData.ReadInt32();
                    binData.Seek(vertexFactoryMapCount * 12, SeekOrigin.Current);

                    int materialShaderMapCount = binData.ReadInt32();
                    for (int i = 0; i < materialShaderMapCount; i++)
                    {
                        binData.Seek(16, SeekOrigin.Current);

                        int switchParamCount = binData.ReadInt32();
                        binData.Seek(switchParamCount * 32, SeekOrigin.Current);

                        int componentMaskParamCount = binData.ReadInt32();
                        binData.Seek(componentMaskParamCount * 44, SeekOrigin.Current);

                        int normalParams = binData.ReadInt32();
                        binData.Seek(normalParams * 29, SeekOrigin.Current);

                        binData.Seek(8, SeekOrigin.Current);

                        int nextMaterialShaderMapOffset = binData.ReadInt32() - oldDataOffset;
                        binData.Seek(-4, SeekOrigin.Current);
                        binData.WriteInt32(nextMaterialShaderMapOffset + newDataOffset);
                        binData.Seek(nextMaterialShaderMapOffset, SeekOrigin.Begin);
                    }

                    export.Data = binData.ToArray();
                    break;
                }
            }
        }

        private void WriteHeader(Stream ms)
        {
            ms.WriteUInt32(packageTagLittleEndian);
            //version
            switch (Game)
            {
                case MEGame.ME1:
                    ms.WriteUInt16(ME1UnrealVersion);
                    ms.WriteUInt16(ME1LicenseeVersion);
                    break;
                case MEGame.ME2:
                    ms.WriteUInt16(ME2UnrealVersion);
                    ms.WriteUInt16(ME2LicenseeVersion);
                    break;
                case MEGame.ME3:
                    ms.WriteUInt16(ME3UnrealVersion);
                    ms.WriteUInt16(ME3LicenseeVersion);
                    break;
            }
            ms.WriteInt32(FullHeaderSize);
            if (Game == MEGame.ME3)
            {
                ms.WriteUnrealStringUnicode("None");
            }
            else
            {
                ms.WriteUnrealStringASCII("None");
            }

            ms.WriteUInt32((uint)Flags);

            if (Game == MEGame.ME3 && Flags.HasFlag(EPackageFlags.Cooked))
            {
                ms.WriteInt32(PackageTypeId);
            }

            ms.WriteInt32(NameCount);
            ms.WriteInt32(NameOffset);
            ms.WriteInt32(ExportCount);
            ms.WriteInt32(ExportOffset);
            ms.WriteInt32(ImportCount);
            ms.WriteInt32(ImportOffset);
            ms.WriteInt32(DependencyTableOffset);

            if (Game == MEGame.ME3)
            {
                ms.WriteInt32(ImportExportGuidsOffset);
                ms.WriteInt32(0); //ImportGuidsCount
                ms.WriteInt32(0); //ExportGuidsCount
                ms.WriteInt32(0); //ThumbnailTableOffset
            }
            ms.WriteGuid(PackageGuid);

            //Write 1 generation
            ms.WriteInt32(1);
            ms.WriteInt32(Gen0ExportCount);
            ms.WriteInt32(Gen0NameCount);
            ms.WriteInt32(Gen0NetworkedObjectCount);

            //engineVersion and cookedContentVersion
            switch (Game)
            {
                case MEGame.ME1:
                    ms.WriteInt32(3240);
                    ms.WriteInt32(47);
                    break;
                case MEGame.ME2:
                    ms.WriteInt32(3607);
                    ms.WriteInt32(64);
                    break;
                case MEGame.ME3:
                    ms.WriteInt32(6383);
                    ms.WriteInt32(196715);
                    break;
            }


            if (Game == MEGame.ME2 || Game == MEGame.ME1)
            {
                ms.WriteInt32(0);
                ms.WriteInt32(47699); //No idea what this is, but it's always 47699
                switch (Game)
                {
                    case MEGame.ME1:
                        ms.WriteInt32(0);
                        ms.WriteInt32(1);
                        break;
                    case MEGame.ME2:
                        ms.WriteInt32(unknown4);
                        ms.WriteInt32(1966080);
                        break;
                }
            }

            switch (Game)
            {
                case MEGame.ME1:
                    ms.WriteInt32(0);
                    ms.WriteInt32(-1);
                    break;
                case MEGame.ME2:
                    ms.WriteInt32(-1);
                    ms.WriteInt32(-1);
                    break;
                case MEGame.ME3:
                    ms.WriteInt32(unknown6);
                    ms.WriteInt32(145358848);
                    break;
            }

            if (Game == MEGame.ME1)
            {
                ms.WriteInt32(-1);
            }

            ms.WriteUInt32((uint)CompressionType.None);
            ms.WriteInt32(0);//numChunks

            ms.WriteUInt32(packageSource);

            if (Game == MEGame.ME2 || Game == MEGame.ME1)
            {
                ms.WriteInt32(0);
            }

            if (Game == MEGame.ME3 || Game == MEGame.ME2)
            {
                //this code is not in me3exp right now
                ms.WriteInt32(AdditionalPackagesToCook.Count);
                foreach (var pname in AdditionalPackagesToCook)
                {
                    if (Game == MEGame.ME2)
                    {
                        //ME2 Uses ASCII
                        ms.WriteUnrealStringASCII(pname);
                    }
                    else
                    {
                        ms.WriteUnrealStringUnicode(pname);
                    }
                }
            }
        }
        private void ReadLocalTLKs()
        {
            LocalTalkFiles.Clear();
            var exportsToLoad = new List<ExportEntry>();
            foreach (var tlkFileSet in Exports.Where(x => x.ClassName == "BioTlkFileSet" && !x.IsDefaultObject).Select(exp => exp.GetBinaryData<BioTlkFileSet>()))
            {
                foreach ((NameReference lang, BioTlkFileSet.BioTlkSet bioTlkSet) in tlkFileSet.TlkSets)
                {
                    if (Properties.Settings.Default.TLKLanguage.Equals(lang, StringComparison.InvariantCultureIgnoreCase))
                    {
                        exportsToLoad.Add(GetUExport(Properties.Settings.Default.TLKGender_IsMale ? bioTlkSet.Male : bioTlkSet.Female));
                        break;
                    }
                }
            }

            foreach (var exp in exportsToLoad)
            {
                //Debug.WriteLine("Loading local TLK: " + exp.GetIndexedFullPath);
                LocalTalkFiles.Add(new TalkFile(exp));
            }
        }

        // TODO: MOVE THIS OUT OF LIB, put into extension method
        public void ConvertTo(MEGame newGame, string tfcPath = null, bool preserveMaterialInstances = false)
        {
            MEGame oldGame = Game;
            var prePropBinary = new List<byte[]>(ExportCount);
            var propCollections = new List<PropertyCollection>(ExportCount);
            var postPropBinary = new List<ObjectBinary>(ExportCount);

            if (oldGame == MEGame.ME1 && newGame != MEGame.ME1)
            {
                int idx = names.IndexOf("BIOC_Base");
                if (idx >= 0)
                {
                    names[idx] = "SFXGame";
                }
            }
            else if (newGame == MEGame.ME1)
            {
                int idx = names.IndexOf("SFXGame");
                if (idx >= 0)
                {
                    names[idx] = "BIOC_Base";
                }
            }

            //fix up Default_ imports
            if (newGame == MEGame.ME3)
            {
                using IMEPackage core = MEPackageHandler.OpenMEPackage(Path.Combine(ME3Directory.cookedPath, "Core.pcc"));
                using IMEPackage engine = MEPackageHandler.OpenMEPackage(Path.Combine(ME3Directory.cookedPath, "Engine.pcc"));
                using IMEPackage sfxGame = MEPackageHandler.OpenMEPackage(Path.Combine(ME3Directory.cookedPath, "SFXGame.pcc"));
                foreach (ImportEntry defImp in imports.Where(imp => imp.ObjectName.Name.StartsWith("Default_")).ToList())
                {
                    string packageName = defImp.FullPath.Split('.')[0];
                    IMEPackage pck = packageName switch
                    {
                        "Core" => core,
                        "Engine" => engine,
                        "SFXGame" => sfxGame,
                        _ => null
                    };
                    if (pck != null && pck.Exports.FirstOrDefault(exp => exp.ObjectName == defImp.ObjectName) is ExportEntry defExp)
                    {
                        List<IEntry> impChildren = defImp.GetChildren();
                        List<IEntry> expChildren = defExp.GetChildren();
                        foreach (IEntry expChild in expChildren)
                        {
                            if (impChildren.FirstOrDefault(imp => imp.ObjectName == expChild.ObjectName) is ImportEntry matchingImp)
                            {
                                impChildren.Remove(matchingImp);
                            }
                            else
                            {
                                AddImport(new ImportEntry(this)
                                {
                                    idxLink = defImp.UIndex,
                                    ClassName = expChild.ClassName,
                                    ObjectName = expChild.ObjectName,
                                    PackageFile = defImp.PackageFile
                                });
                            }
                        }

                        foreach (IEntry impChild in impChildren)
                        {
                            EntryPruner.TrashEntries(this, impChild.GetAllDescendants().Prepend(impChild));
                        }
                    }
                }
            }

            //purge MaterialExpressions
            if (newGame == MEGame.ME3)
            {
                var entriesToTrash = new List<IEntry>();
                foreach (ExportEntry mat in exports.Where(exp => exp.ClassName == "Material").ToList())
                {
                    entriesToTrash.AddRange(mat.GetAllDescendants());
                }
                EntryPruner.TrashEntries(this, entriesToTrash.ToHashSet());
            }

            EntryPruner.TrashIncompatibleEntries(this, oldGame, newGame);

            foreach (ExportEntry export in exports)
            {
                //convert stack, or just get the pre-prop binary if no stack
                prePropBinary.Add(ExportBinaryConverter.ConvertPrePropBinary(export, newGame));

                PropertyCollection props = export.ClassName == "Class" ? null : EntryPruner.RemoveIncompatibleProperties(this, export.GetProperties(), export.ClassName, newGame);
                propCollections.Add(props);

                //convert binary data
                postPropBinary.Add(ExportBinaryConverter.ConvertPostPropBinary(export, newGame, props));

                //writes header in whatever format is correct for newGame
                export.RegenerateHeader(newGame, true);
            }

            Game = newGame;

            for (int i = 0; i < exports.Count; i++)
            {
                exports[i].WritePrePropsAndPropertiesAndBinary(prePropBinary[i], propCollections[i], postPropBinary[i]);
            }

            if (newGame != MEGame.ME3)  //Fix Up Textures before Materials
            {
                foreach (ExportEntry texport in exports.Where(exp => exp.IsTexture()))
                {
                    texport.WriteProperty(new BoolProperty(true, "NeverStream"));
                }
            }
            else if (exports.Any(exp => exp.IsTexture() && Texture2D.GetTexture2DMipInfos(exp, null)
                                                                                .Any(mip => mip.storageType == StorageTypes.pccLZO
                                                                                         || mip.storageType == StorageTypes.pccZlib)))
            {
                //ME3 can't deal with compressed textures in a pcc, so we'll need to stuff them into a tfc
                tfcPath ??= Path.ChangeExtension(FilePath, "tfc");
                string tfcName = Path.GetFileNameWithoutExtension(tfcPath);
                using var tfc = new FileStream(tfcPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                Guid tfcGuid;
                if (tfc.Length >= 16)
                {
                    tfcGuid = tfc.ReadGuid();
                    tfc.SeekEnd();
                }
                else
                {
                    tfcGuid = Guid.NewGuid();
                    tfc.WriteGuid(tfcGuid);
                }

                foreach (ExportEntry texport in exports.Where(exp => exp.IsTexture()))
                {
                    List<Texture2DMipInfo> mips = Texture2D.GetTexture2DMipInfos(texport, null);
                    var offsets = new List<int>();
                    foreach (Texture2DMipInfo mipInfo in mips)
                    {
                        if (mipInfo.storageType == StorageTypes.pccLZO || mipInfo.storageType == StorageTypes.pccZlib)
                        {
                            offsets.Add((int)tfc.Position);
                            byte[] mip = mipInfo.storageType == StorageTypes.pccLZO
                                ? TextureCompression.CompressTexture(Texture2D.GetTextureData(mipInfo), StorageTypes.extZlib)
                                : Texture2D.GetTextureData(mipInfo, false);
                            tfc.WriteFromBuffer(mip);
                        }
                    }
                    offsets.Add((int)tfc.Position);
                    texport.SetBinaryData(ExportBinaryConverter.ConvertTexture2D(texport, Game, offsets, StorageTypes.extZlib));
                    texport.WriteProperty(new NameProperty(tfcName, "TextureFileCacheName"));
                    texport.WriteProperty(tfcGuid.ToGuidStructProp("TFCFileGuid"));
                }
            }
            if (oldGame == MEGame.ME3 && newGame != MEGame.ME3)
            {
                int idx = names.IndexOf("location");
                if (idx >= 0)
                {
                    names[idx] = "Location";
                }
            }
            else if (newGame == MEGame.ME3)
            {
                int idx = names.IndexOf("Location");
                if (idx >= 0)
                {
                    names[idx] = "location";
                }
            }

            if (newGame == MEGame.ME3) //Special handling where materials have been ported between games.
            {

                //change all materials to default material, but try to preserve diff and norm textures
                using var resourcePCC = MEPackageHandler.OpenME3Package(App.CustomResourceFilePath(MEGame.ME3));
                var defaultmaster = resourcePCC.Exports.First(exp => exp.ObjectName == "NormDiffMaterial");
                var materiallist =  exports.Where(exp => exp.ClassName == "Material" || exp.ClassName == "MaterialInstanceConstant").ToList();
                foreach (var mat in materiallist)
                {
                    Debug.WriteLine($"Fixing up {mat.FullPath}");
                    var masterMat = defaultmaster;
                    var hasDefaultMaster = true;
                    UIndex[] textures = Array.Empty<UIndex>();
                    if (mat.ClassName == "Material")
                    {
                        textures = ObjectBinary.From<Material>(mat).SM3MaterialResource.UniformExpressionTextures;
                        switch (mat.FullPath)
                        {
                            case "BioT_Volumetric.LAG_MM_Volumetric":
                            case "BioT_Volumetric.LAG_MM_FalloffSphere":
                            case "BioT_LevelMaster.Materials.Opaque_MM":
                            case "BioT_LevelMaster.Materials.GUI_Lit_MM":
                            case "BioT_LevelMaster.Materials.Signage.MM_GUIMaster_Emissive":
                            case "BioT_LevelMaster.Materials.Signage.MM_GUIMaster_Emissive_Fallback":
                            case "BioT_LevelMaster.Materials.Opaque_Standard_MM":
                            case "BioT_LevelMaster.Tech_Inset_MM":
                            case "BioT_LevelMaster.Tech_Border_MM":
                            case "BioT_LevelMaster.Brushed_Metal":
                                masterMat = resourcePCC.Exports.First(exp => exp.FullPath == mat.FullPath);
                                hasDefaultMaster = false;
                                break;
                            default:
                                break;
                        }
                    }
                    else if (mat.GetProperty<BoolProperty>("bHasStaticPermutationResource")?.Value == true)
                    {
                        if (mat.GetProperty<ObjectProperty>("Parent") is ObjectProperty parentProp && GetEntry(parentProp.Value) is IEntry parent && parent.ClassName == "Material")
                        {
                            switch (parent.FullPath)
                            {
                                case "BioT_LevelMaster.Materials.Opaque_MM":
                                    masterMat = resourcePCC.Exports.First(exp => exp.FullPath == "Materials.Opaque_MM_INST");
                                    hasDefaultMaster = false;
                                    break;
                                case "BIOG_APL_MASTER_MATERIAL.Placeable_MM":
                                    masterMat = resourcePCC.Exports.First(exp => exp.FullPath == "Materials.Placeable_MM_INST");
                                    hasDefaultMaster = false;
                                    break;
                                case "BioT_LevelMaster.Materials.Opaque_Standard_MM":
                                    masterMat = resourcePCC.Exports.First(exp => exp.FullPath == "Materials.Opaque_Standard_MM_INST");
                                    hasDefaultMaster = false;
                                    break;
                                default:
                                    textures = ObjectBinary.From<MaterialInstance>(mat).SM3StaticPermutationResource.UniformExpressionTextures;
                                    break;
                            }

                            if (!hasDefaultMaster && mat.GetProperty<ArrayProperty<StructProperty>>("TextureParameterValues") is ArrayProperty<StructProperty> texParams)
                            {
                                textures = texParams.Select(structProp => new UIndex(structProp.GetProp<ObjectProperty>("ParameterValue")?.Value ?? 0)).ToArray();
                            }

                        }
                    }
                    else if (preserveMaterialInstances)
                    {
                        continue;
                    }
                    else if (mat.GetProperty<ArrayProperty<StructProperty>>("TextureParameterValues") is ArrayProperty<StructProperty> texParams)
                    {
                        textures = texParams.Select(structProp => new UIndex(structProp.GetProp<ObjectProperty>("ParameterValue")?.Value ?? 0)).ToArray();
                    }
                    else if (mat.GetProperty<ObjectProperty>("Parent") is ObjectProperty parentProp && GetEntry(parentProp.Value) is ExportEntry parent && parent.ClassName == "Material")
                    {
                        textures = ObjectBinary.From<Material>(parent).SM3MaterialResource.UniformExpressionTextures;
                    }

                    if(hasDefaultMaster)
                    {
                        EntryImporter.ReplaceExportDataWithAnother(masterMat, mat);
                        int norm = 0;
                        int diff = 0;
                        foreach (UIndex texture in textures)
                        {
                            if (GetEntry(texture) is IEntry tex)
                            {
                                if (diff == 0 && tex.ObjectName.Name.Contains("diff", StringComparison.OrdinalIgnoreCase))
                                {
                                    diff = texture;
                                }
                                else if (norm == 0 && tex.ObjectName.Name.Contains("norm", StringComparison.OrdinalIgnoreCase))
                                {
                                    norm = texture;
                                }
                            }
                        }
                        if (diff == 0)
                        {
                            diff = EntryImporter.GetOrAddCrossImportOrPackage("EngineMaterials.DefaultDiffuse", resourcePCC, this).UIndex;
                        }

                        var matBin = ObjectBinary.From<Material>(mat);
                        matBin.SM3MaterialResource.UniformExpressionTextures = new UIndex[] { norm, diff };
                        mat.SetBinaryData(matBin);
                        mat.Class = imports.First(imp => imp.ObjectName == "Material");
                    }
                    else if (mat.ClassName == "Material")
                    {
                        var mmparent = EntryImporter.GetOrAddCrossImportOrPackage(masterMat.ParentFullPath, resourcePCC, this);
                        EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.CloneAllDependencies, masterMat, this, mmparent, true, out IEntry targetexp);
                        mat.ReplaceAllReferencesToThisOne(targetexp);
                        EntryPruner.TrashEntryAndDescendants(mat);
                    }
                    else if (mat.ClassName == "MaterialInstanceConstant")
                    {
                        try
                        {
                            var matprops = mat.GetProperties();
                            var parentlightguid = masterMat.GetProperty<StructProperty>("ParentLightingGuid");
                            matprops.AddOrReplaceProp(parentlightguid);
                            var mguid = masterMat.GetProperty<StructProperty>("m_Guid");
                            matprops.AddOrReplaceProp(mguid);
                            var lguid = masterMat.GetProperty<StructProperty>("LightingGuid");
                            matprops.AddOrReplaceProp(lguid);
                            var masterBin = ObjectBinary.From<MaterialInstance>(masterMat);
                            var matBin = ObjectBinary.From<MaterialInstance>(mat);
                            var staticResTextures3 = masterBin.SM3StaticPermutationResource.UniformExpressionTextures.ToList();
                            var newtextures3 = new List<UIndex>();
                            var staticResTextures2 = masterBin.SM2StaticPermutationResource.UniformExpressionTextures.ToList();
                            var newtextures2 = new List<UIndex>();
                            IEntry norm = null;
                            IEntry diff = null;
                            IEntry spec = null;
                            foreach (var texref in textures)
                            {
                                IEntry texEnt = this.GetEntry(texref);
                                string texName = texEnt?.ObjectName ?? "None";
                                if (texName.ToLowerInvariant().Contains("norm"))
                                    norm = texEnt;
                                else if (texName.ToLowerInvariant().Contains("diff"))
                                    diff = texEnt;
                                else if(texName.ToLowerInvariant().Contains("spec"))
                                    spec = texEnt;
                                else if(texName.ToLowerInvariant().Contains("msk"))
                                    spec = texEnt;
                            }

                            foreach (var texidx in staticResTextures2)
                            {
                                var masterTxt = resourcePCC.GetEntry(texidx);
                                IEntry newTxtEnt = masterTxt;
                                switch (masterTxt?.ObjectName)
                                {
                                    case "DefaultDiffuse":
                                        if (diff != null)
                                            newTxtEnt = diff;
                                        break;
                                    case "DefaultNormal":
                                        if (norm != null)
                                            newTxtEnt = norm;
                                        break;
                                    case "Gray":  //Spec
                                        if (spec != null)
                                            newTxtEnt = spec;
                                        break;
                                    default:
                                        break;
                                }
                                var newtexidx = Exports.FirstOrDefault(x => x.FullPath == newTxtEnt.FullPath)?.UIndex ?? 0;
                                if (newtexidx == 0)
                                    newtexidx = Imports.FirstOrDefault(x => x.FullPath == newTxtEnt.FullPath)?.UIndex ?? 0;
                                if (newTxtEnt == masterTxt && newtexidx == 0)
                                {
                                    var texparent = EntryImporter.GetOrAddCrossImportOrPackage(newTxtEnt.ParentFullPath, resourcePCC, this);
                                    EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.CloneAllDependencies, newTxtEnt, this, texparent, true, out IEntry newtext);
                                    newtextures2.Add(newtext?.UIndex ?? 0);
                                }
                                else
                                {
                                    newtextures2.Add(newtexidx);
                                }
                            }

                            foreach (var texidx in staticResTextures3)
                            {
                                var masterTxt = resourcePCC.GetEntry(texidx);
                                IEntry newTxtEnt = masterTxt;
                                switch (masterTxt?.ObjectName)
                                {
                                    case "DefaultDiffuse":
                                        if (diff != null)
                                            newTxtEnt = diff;
                                        break;
                                    case "DefaultNormal":
                                        if (norm != null)
                                            newTxtEnt = norm;
                                        break;
                                    case "Gray":  //Spec
                                        if (spec != null)
                                            newTxtEnt = spec;
                                        break;
                                    default:
                                        break;
                                }
                                var newtexidx = Exports.FirstOrDefault(x => x.FullPath == newTxtEnt.FullPath)?.UIndex ?? 0;
                                if(newtexidx == 0)
                                    newtexidx = Imports.FirstOrDefault(x => x.FullPath == newTxtEnt.FullPath)?.UIndex ?? 0;
                                if (newTxtEnt == masterTxt && newtexidx == 0)
                                {
                                    var texparent = EntryImporter.GetOrAddCrossImportOrPackage(newTxtEnt.ParentFullPath, resourcePCC, this);
                                    EntryImporter.ImportAndRelinkEntries(EntryImporter.PortingOption.CloneAllDependencies, newTxtEnt, this, texparent, true, out IEntry newtext);
                                    newtextures3.Add(newtext?.UIndex ?? 0);
                                }
                                else
                                {
                                    newtextures3.Add(newtexidx);
                                }
                            }
                            masterBin.SM2StaticPermutationResource.UniformExpressionTextures = newtextures2.ToArray();
                            masterBin.SM3StaticPermutationResource.UniformExpressionTextures = newtextures3.ToArray();
                            mat.WritePropertiesAndBinary(matprops, masterBin);
                        }
                        catch
                        {
                            Debug.WriteLine("MaterialInstanceConversion error");
                        }
                    }
                }
            }
        }
    }
}