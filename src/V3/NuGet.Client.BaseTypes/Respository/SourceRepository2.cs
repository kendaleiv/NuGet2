﻿using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.Runtime.Versioning;
using NuGet.Client.BaseTypes.Respository;
using System.ComponentModel.Composition;

namespace NuGet.Client
{
  /// <summary>
    /// Represents a Server endpoint. Exposes the list of resources/services provided by the endpoint like : Search service, Metrics service and so on.
    /// </summary>
    // TODO: it needs to implement IDisposable.
    // *TODOs: Define RequiredResourceNotFound exception instead of general exception.   
    // *TODOs: Uninstall newtonsoft.json
    public  class SourceRepository2
    {
        [ImportMany]
        private IEnumerable<Lazy<IResourceProvider, IResourceProviderMetadata>> _providers { get; set; }
        private readonly PackageSource _source;
        private static IDictionary<string, object> _cache = new Dictionary<string, object>();

        public SourceRepository2(PackageSource source, IEnumerable<Lazy<IResourceProvider, IResourceProviderMetadata>> providers)
        {
            _source = source;
            _providers = providers;
        }

        public object GetResource(Type resourceType)
        {            
            foreach(Lazy<IResourceProvider,IResourceProviderMetadata>  provider in _providers)
            {
                if (provider.Metadata.ResourceType == resourceType)
                {
                    Resource resource = null;
                    if (provider.Value.TryCreateResource(_source, ref _cache, out resource))
                    {
                        return resource;
                    }
                }
            }
            return null;
        }
       
        public T GetResource<T>() { return (T)GetResource(typeof(T)); }
    }
}