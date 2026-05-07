#if !NETSTANDARD
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Microsoft.Maui.HotReload.MauiMetadataUpdateHandler))]
#endif
