﻿using Inedo.UPack;
using Inedo.UPack.Packaging;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Inedo.ProGet.UPack
{
    [DisplayName("repack")]
    [Description("Creates a new ProGet universal package from an existing package with optionally modified metadata.")]
    public sealed class Repack : Command
    {
        [DisplayName("manifest")]
        [AlternateName("metadata")]
        [Description("Path of upack.json file to merge.")]
        [ExtraArgument]
        public string Manifest { get; set; }

        [DisplayName("source")]
        [Description("The path of the existing upack file.")]
        [PositionalArgument(0)]
        [ExpandPath]
        public string SourcePath { get; set; }

        [DisplayName("targetDirectory")]
        [Description("Directory where the .upack file will be created. If not specified, the current working directory is used.")]
        [ExtraArgument]
        [ExpandPath]
        public string TargetDirectory { get; set; }

        [DisplayName("group")]
        [Description("Package group. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string Group { get; set; }

        [DisplayName("name")]
        [Description("Package name. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string Name { get; set; }

        [DisplayName("version")]
        [Description("Package version. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string Version { get; set; }

        [DisplayName("title")]
        [Description("Package title. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string Title { get; set; }

        [DisplayName("description")]
        [Description("Package description. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string PackageDescription { get; set; }

        [DisplayName("icon")]
        [Description("Icon absolute Url. If metadata file is provided, value will be ignored.")]
        [ExtraArgument]
        public string IconUrl { get; set; }

        [DisplayName("overwrite")]
        [Description("Overwrite existing package file if it already exists.")]
        [ExtraArgument]
        [DefaultValue(false)]
        public bool Overwrite { get; set; }

        public override async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            var info = CloneExistingPackageMetadata();
            var infoToMerge = await GetMetadataToMergeAsync();

            foreach (var modifiedProperty in infoToMerge)
                info[modifiedProperty.Key] = modifiedProperty.Value;

            var error = ValidateManifest(info);
            if (error != null)
            {
                Console.Error.WriteLine("Invalid {0}: {1}", string.IsNullOrWhiteSpace(this.Manifest) ? "parameters" : "upack.json", error);
                return 2;
            }

            PrintManifest(info);

            string relativePackageFileName = $"{info.Name}-{info.Version.Major}.{info.Version.Minor}.{info.Version.Patch}.upack";
            string targetFileName = Path.Combine(this.TargetDirectory ?? Environment.CurrentDirectory, relativePackageFileName);

            if (!this.Overwrite && File.Exists(targetFileName))
                throw new UpackException($"Target file '{targetFileName}' exists and overwrite was set to false.");

            string tmpPath = Path.GetTempFileName();

            using (var existingPackage = new UniversalPackage(this.SourcePath))
            using (var builder = new UniversalPackageBuilder(tmpPath, info))
            {
                var entries = from e in existingPackage.Entries
                              where !string.Equals(e.RawPath, "upack.json", StringComparison.OrdinalIgnoreCase)
                              select e;

                foreach (var entry in entries)
                {
                    if (entry.IsDirectory)
                    {
                        builder.AddEmptyDirectoryRaw(entry.RawPath);
                    }
                    else
                    {
                        using (var stream = entry.Open())
                        {
                            await builder.AddFileRawAsync(stream, entry.RawPath, entry.Timestamp, cancellationToken);
                        }
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFileName));
            File.Delete(targetFileName);
            File.Move(tmpPath, targetFileName);

            return 0;
        }

        private async Task<UniversalPackageMetadata> GetMetadataToMergeAsync()
        {
            if (string.IsNullOrWhiteSpace(this.Manifest))
            {
                return new UniversalPackageMetadata
                {
                    Group = this.Group,
                    Name = this.Name,
                    Version = UniversalPackageVersion.TryParse(this.Version),
                    Title = this.Title,
                    Description = this.PackageDescription,
                    Icon = this.IconUrl
                };
            }
            else
            {
                try
                {
                    using (var metadataStream = File.OpenRead(this.Manifest))
                    {
                        return await ReadManifestAsync(metadataStream);
                    }
                }
                catch (Exception ex)
                {
                    throw new UpackException($"The manifest file '{this.Manifest}' does not exist or could not be opened.", ex);
                }
            }
        }

        private UniversalPackageMetadata CloneExistingPackageMetadata()
        {
            try
            {
                using (var package = new UniversalPackage(this.SourcePath))
                {
                    return package.GetFullMetadata().Clone();
                }
            }
            catch (Exception ex)
            {
                throw new UpackException($"The source package file '{this.SourcePath}' does not exist or could not be opened.", ex);
            }
        }
    }
}