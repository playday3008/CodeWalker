using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

using SharpDX;

namespace CodeWalker.Cli;

internal static class InspectHandler
{
    public static Command CreateCommand()
    {
        RpfCommandOptions rpfOpts = new();
        Argument<string> pathArg = new("path")
        {
            Description = "Path of the file within the RPF archive",
        };

        Command command = new(
            "inspect",
            "Show detailed metadata for a specific file in an RPF archive"
        )
        {
            pathArg,
        };
        rpfOpts.AddTo(command);
        command.Aliases.Add("i");

        command.SetAction(parseResult =>
            Execute(rpfOpts.Parse(parseResult), parseResult.GetRequiredValue(pathArg))
        );

        return command;
    }

    public static int Execute(RpfOptions options, string filePath)
    {
        Json.InspectResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.RpfPath,
                Path = filePath,
                Name = "",
                Size = 0,
                SizeFormatted = "0 B",
                Type = "",
                Extension = "",
                NameHash = 0,
                ShortNameHash = 0,
                ErrorMessages = errorMessages,
            };

        string? initError = RpfService.ValidateAndLoadKeys(
            options.RpfPath,
            options.ExePath,
            options.Gen9,
            options.Json
        );
        if (initError != null)
        {
            return RpfService.ReportError(initError, options.Json, ErrorResult([]));
        }

        try
        {
            List<string> scanErrors = [];
            RpfFile rpf = RpfService.OpenRpf(
                options.RpfPath,
                options.Verbose,
                options.Json,
                scanErrors
            );

            if (!options.Json)
            {
                Console.Error.WriteLine();
            }

            // Find entry by normalized path
            string normalizedPath = filePath.Replace('\\', '/');
            RpfFileEntry? found = FindEntry(rpf, normalizedPath, options.Recursive);

            if (found == null)
            {
                return RpfService.ReportError(
                    $"File not found in archive: {filePath}",
                    options.Json,
                    ErrorResult([])
                );
            }

            long size = found.GetFileSize();
            string ext = Path.GetExtension(found.Name).ToLowerInvariant();
            string fileType = RpfService.GetFileType(found);

            // Extract type-specific metadata
            int? resourceVersion = null;
            long? systemSize = null;
            long? graphicsSize = null;
            long? uncompressedSize = null;
            uint? encryptionType = null;

            if (found is RpfResourceFileEntry rfe)
            {
                resourceVersion = rfe.Version;
                systemSize = rfe.SystemSize;
                graphicsSize = rfe.GraphicsSize;
            }
            else if (found is RpfBinaryFileEntry bfe)
            {
                uncompressedSize = bfe.FileUncompressedSize;
                encryptionType = bfe.EncryptionType;
            }

            Json.InspectResult result = new()
            {
                Success = scanErrors.Count == 0,
                RpfFile = options.RpfPath,
                Path = found.Path,
                Name = found.Name,
                Size = size,
                SizeFormatted = options.SizeFormat.ToFormattedString(size),
                Type = fileType,
                Extension = ext,
                NameHash = found.NameHash,
                ShortNameHash = found.ShortNameHash,
                ResourceVersion = resourceVersion,
                SystemSize = systemSize,
                GraphicsSize = graphicsSize,
                UncompressedSize = uncompressedSize,
                EncryptionType = encryptionType,
                Details = GetDetails(found, ext, options.Verbose),
                ErrorMessages = [.. scanErrors],
            };

            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                PrintTextResult(result, options);
            }

            return scanErrors.Count > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Json,
                ErrorResult([]),
                options.Verbose ? ex.StackTrace : null
            );
        }
    }

    private static RpfFileEntry? FindEntry(RpfFile rpf, string normalizedPath, bool recursive)
    {
        if (rpf.AllEntries != null)
        {
            foreach (RpfEntry entry in rpf.AllEntries)
            {
                if (
                    entry is RpfFileEntry fileEntry
                    && entry.Path?.Replace('\\', '/').Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) == true
                )
                {
                    return fileEntry;
                }
            }
        }

        if (recursive && rpf.Children != null)
        {
            foreach (RpfFile child in rpf.Children)
            {
                RpfFileEntry? found = FindEntry(child, normalizedPath, recursive);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static object? GetDetails(RpfFileEntry entry, string ext, bool verbose)
    {
        try
        {
            return ext switch
            {
                ".ytd" => GetYtdDetails(entry),
                ".ydr" => GetYdrDetails(entry),
                ".ydd" => GetYddDetails(entry),
                ".yft" => GetYftDetails(entry),
                ".ymap" => GetYmapDetails(entry),
                ".ytyp" => GetYtypDetails(entry),
                ".ybn" => GetYbnDetails(entry),
                ".awc" => GetAwcDetails(entry),
                ".gxt2" => GetGxt2Details(entry),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                Console.Error.WriteLine($"Warning: Failed to read details for {entry.Path}: {ex.Message}");
            }
            return null;
        }
    }

    private static Json.YtdDetails? GetYtdDetails(RpfFileEntry entry)
    {
        YtdFile file = RpfFile.GetFile<YtdFile>(entry);
        if (file?.TextureDict?.Textures?.data_items == null)
            return null;

        Texture[] textures = file.TextureDict.Textures.data_items;
        List<Json.TextureInfo> infos = [];
        foreach (Texture tex in textures)
        {
            if (tex == null)
                continue;
            infos.Add(
                new Json.TextureInfo
                {
                    Name = tex.Name ?? "",
                    Width = tex.Width,
                    Height = tex.Height,
                    Format = tex.Format.ToString(),
                    MipLevels = tex.Levels,
                    Stride = tex.Stride,
                }
            );
        }

        return new Json.YtdDetails { TextureCount = infos.Count, Textures = infos };
    }

    private static Json.YdrDetails? GetYdrDetails(RpfFileEntry entry)
    {
        YdrFile file = RpfFile.GetFile<YdrFile>(entry);
        if (file?.Drawable?.DrawableModels == null)
            return null;

        return new Json.YdrDetails { Lods = GetLodInfos(file.Drawable.DrawableModels) };
    }

    private static Json.YddDetails? GetYddDetails(RpfFileEntry entry)
    {
        YddFile file = RpfFile.GetFile<YddFile>(entry);
        if (file?.DrawableDict?.Drawables?.data_items == null)
            return null;

        Drawable?[] drawables = file.DrawableDict.Drawables.data_items;
        List<Json.DrawableInfo> infos = [];
        foreach (Drawable? d in drawables)
        {
            if (d == null)
                continue;
            long verts = 0;
            long tris = 0;
            if (d.AllModels != null)
            {
                foreach (DrawableModel? model in d.AllModels)
                {
                    if (model?.Geometries == null)
                        continue;
                    foreach (DrawableGeometry? geom in model.Geometries)
                    {
                        verts += geom.VerticesCount;
                        tris += geom.TrianglesCount;
                    }
                }
            }
            infos.Add(
                new Json.DrawableInfo
                {
                    Name = d.Name ?? "",
                    TotalVertices = verts,
                    TotalTriangles = tris,
                }
            );
        }

        return new Json.YddDetails { DrawableCount = infos.Count, Drawables = infos };
    }

    private static Json.YftDetails? GetYftDetails(RpfFileEntry entry)
    {
        YftFile file = RpfFile.GetFile<YftFile>(entry);
        if (file?.Fragment == null)
            return null;

        List<Json.LodInfo> lods = [];
        if (file.Fragment.Drawable?.DrawableModels != null)
        {
            lods = GetLodInfos(file.Fragment.Drawable.DrawableModels);
        }

        return new Json.YftDetails
        {
            Lods = lods,
            HasDrawableCloth = file.Fragment.DrawableCloth != null,
        };
    }

    private static Json.YmapDetails? GetYmapDetails(RpfFileEntry entry)
    {
        YmapFile file = RpfFile.GetFile<YmapFile>(entry);
        if (file == null)
            return null;

        string? entExtMin = null;
        string? entExtMax = null;
        string? strExtMin = null;
        string? strExtMax = null;

        if (file._CMapData.entitiesExtentsMin != default(Vector3))
            entExtMin = FormatVector3(file._CMapData.entitiesExtentsMin);
        if (file._CMapData.entitiesExtentsMax != default(Vector3))
            entExtMax = FormatVector3(file._CMapData.entitiesExtentsMax);
        if (file._CMapData.streamingExtentsMin != default(Vector3))
            strExtMin = FormatVector3(file._CMapData.streamingExtentsMin);
        if (file._CMapData.streamingExtentsMax != default(Vector3))
            strExtMax = FormatVector3(file._CMapData.streamingExtentsMax);

        return new Json.YmapDetails
        {
            EntityCount = file.AllEntities?.Length ?? 0,
            CarGeneratorCount = file.CarGenerators?.Length ?? 0,
            EntitiesExtentsMin = entExtMin,
            EntitiesExtentsMax = entExtMax,
            StreamingExtentsMin = strExtMin,
            StreamingExtentsMax = strExtMax,
            IsScripted = file.IsScripted,
        };
    }

    private static Json.YtypDetails? GetYtypDetails(RpfFileEntry entry)
    {
        YtypFile file = RpfFile.GetFile<YtypFile>(entry);
        if (file?.AllArchetypes == null)
            return null;

        int baseCount = 0;
        int timeCount = 0;
        int mloCount = 0;
        List<Json.MloInfo> mloDetails = [];

        foreach (Archetype? arch in file.AllArchetypes)
        {
            if (arch is MloArchetype mlo)
            {
                mloCount++;
                mloDetails.Add(
                    new Json.MloInfo
                    {
                        Name = mlo.Hash.ToString(),
                        EntityCount = mlo.entities?.Length ?? 0,
                        RoomCount = mlo.rooms?.Length ?? 0,
                        PortalCount = mlo.portals?.Length ?? 0,
                    }
                );
            }
            else if (arch is TimeArchetype)
            {
                timeCount++;
            }
            else
            {
                baseCount++;
            }
        }

        return new Json.YtypDetails
        {
            ArchetypeCount = file.AllArchetypes.Length,
            BaseCount = baseCount,
            TimeCount = timeCount,
            MloCount = mloCount,
            MloDetails = mloDetails.Count > 0 ? mloDetails : null,
        };
    }

    private static Json.YbnDetails? GetYbnDetails(RpfFileEntry entry)
    {
        YbnFile file = RpfFile.GetFile<YbnFile>(entry);
        if (file?.Bounds == null)
            return null;

        int? childCount = null;
        if (file.Bounds is BoundComposite composite)
        {
            childCount = composite.Children?.data_items?.Length ?? 0;
        }

        return new Json.YbnDetails
        {
            BoundsType = file.Bounds.Type.ToString(),
            ChildCount = childCount,
        };
    }

    private static Json.AwcDetails? GetAwcDetails(RpfFileEntry entry)
    {
        AwcFile file = RpfFile.GetFile<AwcFile>(entry);
        if (file?.Streams == null)
            return null;

        List<Json.AwcStreamInfo> infos = [];
        foreach (AwcStream? stream in file.Streams)
        {
            if (stream?.StreamInfo == null)
                continue;

            AwcFormatChunk? fmt = stream.FormatChunk;
            infos.Add(
                new Json.AwcStreamInfo
                {
                    Id = stream.StreamInfo.Id,
                    SamplesPerSecond = fmt?.SamplesPerSecond ?? 0,
                    Codec = fmt?.Codec.ToString() ?? "unknown",
                    Samples = fmt?.Samples ?? 0,
                }
            );
        }

        return new Json.AwcDetails { StreamCount = infos.Count, Streams = infos };
    }

    private static Json.Gxt2Details? GetGxt2Details(RpfFileEntry entry)
    {
        Gxt2File file = RpfFile.GetFile<Gxt2File>(entry);
        if (file?.TextEntries == null)
            return null;

        List<Json.Gxt2EntryInfo> infos = [];
        int limit = Math.Min(file.TextEntries.Length, 50);
        for (int i = 0; i < limit; i++)
        {
            Gxt2Entry e = file.TextEntries[i];
            string text = e.Text ?? "";
            if (text.Length > 100)
                text = text[..100] + "...";

            infos.Add(new Json.Gxt2EntryInfo { Hash = $"0x{e.Hash:X8}", Text = text });
        }

        return new Json.Gxt2Details { EntryCount = file.TextEntries.Length, Entries = infos };
    }

    private static List<Json.LodInfo> GetLodInfos(DrawableModelsBlock models)
    {
        List<Json.LodInfo> lods = [];
        AddLod(lods, "High", models.High);
        AddLod(lods, "Med", models.Med);
        AddLod(lods, "Low", models.Low);
        AddLod(lods, "VLow", models.VLow);
        return lods;
    }

    private static void AddLod(List<Json.LodInfo> lods, string level, DrawableModel[]? models)
    {
        if (models == null || models.Length == 0)
            return;

        int geomCount = 0;
        long totalVerts = 0;
        long totalTris = 0;

        foreach (DrawableModel model in models)
        {
            if (model?.Geometries == null)
                continue;
            geomCount += model.Geometries.Length;
            foreach (DrawableGeometry? geom in model.Geometries)
            {
                totalVerts += geom.VerticesCount;
                totalTris += geom.TrianglesCount;
            }
        }

        lods.Add(
            new Json.LodInfo
            {
                Level = level,
                ModelCount = models.Length,
                GeometryCount = geomCount,
                TotalVertices = totalVerts,
                TotalTriangles = totalTris,
            }
        );
    }

    private static string FormatVector3(Vector3 v)
    {
        return $"{v.X:F2}, {v.Y:F2}, {v.Z:F2}";
    }

    private static void PrintTextResult(Json.InspectResult result, RpfOptions options)
    {
        Console.WriteLine($"Path:       {result.Path}");
        Console.WriteLine($"Name:       {result.Name}");
        Console.WriteLine($"Size:       {result.SizeFormatted} ({result.Size} bytes)");
        Console.WriteLine($"Type:       {result.Type}");
        Console.WriteLine($"Extension:  {result.Extension}");
        Console.WriteLine($"NameHash:   0x{result.NameHash:X8}");
        Console.WriteLine($"ShortHash:  0x{result.ShortNameHash:X8}");

        if (result.ResourceVersion != null)
        {
            Console.WriteLine($"Version:    {result.ResourceVersion}");
            Console.WriteLine($"SystemSize: {result.SystemSize}");
            Console.WriteLine($"GraphSize:  {result.GraphicsSize}");
        }

        if (result.UncompressedSize != null)
        {
            Console.WriteLine(
                $"Uncompressed: {options.SizeFormat.ToFormattedString(result.UncompressedSize.Value)}"
            );
            Console.WriteLine($"Encryption:   {result.EncryptionType}");
        }

        if (result.Details == null)
            return;

        Console.WriteLine();

        switch (result.Details)
        {
            case Json.YtdDetails ytd:
                Console.WriteLine($"Textures: {ytd.TextureCount}");
                foreach (Json.TextureInfo tex in ytd.Textures)
                {
                    Console.WriteLine(
                        $"  {tex.Name}: {tex.Width}x{tex.Height} {tex.Format} mips={tex.MipLevels} stride={tex.Stride}"
                    );
                }
                break;

            case Json.YdrDetails ydr:
                PrintLods(ydr.Lods);
                break;

            case Json.YddDetails ydd:
                Console.WriteLine($"Drawables: {ydd.DrawableCount}");
                foreach (Json.DrawableInfo d in ydd.Drawables)
                {
                    Console.WriteLine(
                        $"  {d.Name}: {d.TotalVertices} vertices, {d.TotalTriangles} triangles"
                    );
                }
                break;

            case Json.YftDetails yft:
                PrintLods(yft.Lods);
                Console.WriteLine($"DrawableCloth: {(yft.HasDrawableCloth ? "yes" : "no")}");
                break;

            case Json.YmapDetails ymap:
                Console.WriteLine($"Entities:       {ymap.EntityCount}");
                Console.WriteLine($"Car Generators: {ymap.CarGeneratorCount}");
                if (ymap.EntitiesExtentsMin != null)
                    Console.WriteLine(
                        $"Entity Extents: [{ymap.EntitiesExtentsMin}] to [{ymap.EntitiesExtentsMax}]"
                    );
                if (ymap.StreamingExtentsMin != null)
                    Console.WriteLine(
                        $"Stream Extents: [{ymap.StreamingExtentsMin}] to [{ymap.StreamingExtentsMax}]"
                    );
                Console.WriteLine($"Scripted:       {(ymap.IsScripted ? "yes" : "no")}");
                break;

            case Json.YtypDetails ytyp:
                Console.WriteLine($"Archetypes: {ytyp.ArchetypeCount}");
                Console.WriteLine(
                    $"  Base: {ytyp.BaseCount}, Time: {ytyp.TimeCount}, MLO: {ytyp.MloCount}"
                );
                if (ytyp.MloDetails != null)
                {
                    foreach (Json.MloInfo mlo in ytyp.MloDetails)
                    {
                        Console.WriteLine(
                            $"  MLO {mlo.Name}: {mlo.EntityCount} entities, {mlo.RoomCount} rooms, {mlo.PortalCount} portals"
                        );
                    }
                }
                break;

            case Json.YbnDetails ybn:
                Console.WriteLine($"Bounds Type: {ybn.BoundsType}");
                if (ybn.ChildCount != null)
                    Console.WriteLine($"Children:    {ybn.ChildCount}");
                break;

            case Json.AwcDetails awc:
                Console.WriteLine($"Streams: {awc.StreamCount}");
                foreach (Json.AwcStreamInfo s in awc.Streams)
                {
                    Console.WriteLine(
                        $"  Stream {s.Id}: {s.Codec} {s.SamplesPerSecond}Hz {s.Samples} samples"
                    );
                }
                break;

            case Json.Gxt2Details gxt2:
                Console.WriteLine($"Text Entries: {gxt2.EntryCount}");
                foreach (Json.Gxt2EntryInfo e in gxt2.Entries)
                {
                    Console.WriteLine($"  {e.Hash}: {e.Text}");
                }
                if (gxt2.EntryCount > gxt2.Entries.Count)
                    Console.WriteLine($"  ... and {gxt2.EntryCount - gxt2.Entries.Count} more");
                break;
        }
    }

    private static void PrintLods(IReadOnlyList<Json.LodInfo> lods)
    {
        foreach (Json.LodInfo lod in lods)
        {
            Console.WriteLine(
                $"  {lod.Level}: {lod.ModelCount} models, {lod.GeometryCount} geometries, {lod.TotalVertices} vertices, {lod.TotalTriangles} triangles"
            );
        }
    }
}
