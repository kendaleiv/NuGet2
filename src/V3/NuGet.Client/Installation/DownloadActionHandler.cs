﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Client.Diagnostics;
using NewPackageAction = NuGet.Client.Resolution.PackageAction;

namespace NuGet.Client.Installation
{
    /// <summary>
    /// Handles the Download package Action by downloading the specified package into the shared repository
    /// (packages folder) for the solution.
    /// </summary>
    public class DownloadActionHandler : IActionHandler
    {
        public Task Execute(NewPackageAction action, IExecutionLogger logger)
        {
            Uri downloadUri;
            try
            {
                downloadUri = action.Package.Value<Uri>(Properties.NupkgUrl);
            }
            catch (UriFormatException urifx)
            {
                throw new InvalidOperationException(String.Format(
                    CultureInfo.CurrentCulture,
                    Strings.DownloadActionHandler_InvalidDownloadUrl,
                    action.PackageIdentity,
                    action.Package[Properties.NupkgUrl].ToString(),
                    urifx.Message));
            }
            if (downloadUri == null)
            {
                throw new InvalidOperationException(String.Format(
                    CultureInfo.CurrentCulture,
                    Strings.DownloadActionHandler_NoDownloadUrl,
                    action.PackageIdentity));
            }

            return Task.Run(() =>
            {
                // Get required features from the target
                var packageManager = action.Target.GetRequiredFeature<IPackageManager>();
                var packageCache = action.Target.GetRequiredFeature<IPackageCacheRepository>();

                // Load the package
                var package = GetPackage(packageCache, action.PackageIdentity, downloadUri);

                NuGetTraceSources.ActionExecutor.Verbose(
                    "download/loadedpackage",
                    "[{0}] Loaded package.",
                    action.PackageIdentity);
                
                // Now convert the action and use the V2 Execution logic since we
                // have a true V2 IPackage (either from the cache or an in-memory ZipPackage).
                packageManager.Logger = new ShimLogger(logger);
                packageManager.Execute(new PackageOperation(
                    package,
                    NuGet.PackageAction.Install));

                // Run init.ps1 if present. Init is run WITHOUT specifying a target framework.
                ActionHandlerHelpers.ExecutePowerShellScriptIfPresent(
                    "init.ps1",
                    action.Target,
                    package,
                    packageManager.PathResolver.GetInstallPath(package),
                    logger);
            });
        }

        public Task Rollback(NewPackageAction action, IExecutionLogger logger)
        {
            // Just run the purge action to undo a download
            return new PurgeActionHandler().Execute(action, logger);
        }

        private IPackage GetPackage(IPackageCacheRepository packageCache, PackageIdentity packageIdentity, Uri downloadUri)
        {
            var packageSemVer = CoreConverters.SafeToSemVer(packageIdentity.Version);
            var packageName = CoreConverters.SafeToPackageName(packageIdentity);
            var downloader = new PackageDownloader();

            var package = packageCache.FindPackage(packageName.Id, packageSemVer);
            if (package != null)
            {
                NuGetTraceSources.ActionExecutor.Info(
                    "download/cachehit",
                    "[{0}] Download: Cache Hit!",
                    packageIdentity);
                // Success!
                return package;
            }

            if (downloadUri == null)
            {
                throw new InvalidOperationException(String.Format(
                    CultureInfo.CurrentCulture,
                    Strings.DownloadActionHandler_NoDownloadUrl,
                    packageIdentity));
            }

            // Try to download the package through the cache.
            bool success = packageCache.InvokeOnPackage(
                packageIdentity.Id,
                packageSemVer,
                (targetStream) =>
                    downloader.DownloadPackage(
                        new HttpClient(downloadUri),
                        packageName,
                        targetStream));
            if (success)
            {
                NuGetTraceSources.ActionExecutor.Info(
                    "download/downloadedtocache",
                    "[{0}] Download: Downloaded to cache",
                    packageName);

                // Try to get it from the cache again
                package = packageCache.FindPackage(packageIdentity.Id, packageSemVer);
            }

            // Either:
            //  1. We failed to load the package into the cache, which can happen when 
            //       access to the %LocalAppData% directory is blocked, 
            //       e.g. on Windows Azure Web Site build OR
            //  B. It was purged from the cache before it could be retrieved again.
            // Regardless, the cache isn't working for us, so download it in to memory.
            if (package == null)
            {
                NuGetTraceSources.ActionExecutor.Info(
                    "download/cachefailing",
                    "[{0}] Download: Cache isn't working. Downloading to RAM",
                    packageName);

                using (var targetStream = new MemoryStream())
                {
                    downloader.DownloadPackage(
                        new HttpClient(downloadUri),
                        packageName,
                        targetStream);

                    targetStream.Seek(0, SeekOrigin.Begin);
                    package = new ZipPackage(targetStream);
                }
            }
            return package;
        }
    }
}