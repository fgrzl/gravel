Configuration examples for Gravel

This file shows minimal examples for binding `GravelOptions` and how to set separate paths for WAL and SST files.

1) appsettings.json example

```
{
  "Gravel": {
    "DatabasePath": "C:\\data\\graveldb",
    // Optional: put WAL files on a different disk or folder for performance/durability
    "WalPath": "D:\\wal-storage\\graveldb-wal",
    // Optional: put SST files on a different disk/folder
    "SstPath": "E:\\ssd-storage\\graveldb-sst",

    "MemTableThreshold": 4096,
    "SstLevels": 7,
    "WalSegmentSize": 16777216, // 16MB
    "WalSyncOnCommit": true,
    "CompactionFanInThreshold": 4
  }
}
```

2) Program.cs (ASP.NET Core / Generic Host) - options binding and service registration

```
// var builder = WebApplication.CreateBuilder(args);
// Configuration is already loaded into builder.Configuration

// Configure options from the "Gravel" section
builder.Services.Configure<Gravel.Abstractions.GravelOptions>(
    builder.Configuration.GetSection("Gravel")
);

// Register storage factories and engine (example service registrations)
// Use your concrete implementations (file system or in-memory)
builder.Services.AddSingleton<Gravel.Abstractions.Storage.Wal.IWalFactory, Gravel.Storage.FileSystem.Wal.FileWalFactory>();
builder.Services.AddSingleton<Gravel.Abstractions.Storage.Sst.ISstFactory, Gravel.Storage.FileSystem.Sst.FileSstFactory>();

// Add the engine (resolve GravelOptions via IOptions<T>)
builder.Services.AddSingleton<Gravel.Engine.IGravelEngine, Gravel.Engine.Engine>();

// Later, when the Engine is constructed, it will read WalPath and SstPath from GravelOptions
// and fall back to DatabasePath/wal and DatabasePath/sst when not provided.
```

3) Environment variables

You can also override values using environment variables. The keys use the configuration path with `:` separators, for example:

- `Gravel:DatabasePath` -> path
- `Gravel:WalPath` -> wal path
- `Gravel:SstPath` -> sst path

Example (PowerShell):

```
$env:Gravel__DatabasePath = 'C:\data\graveldb'
$env:Gravel__WalPath = 'D:\wal-storage\graveldb-wal'
```

Notes

- If you provide `WalPath` and/or `SstPath`, those directories will be used by the registered storage factories; otherwise the engine will use `DatabasePath/wal` and `DatabasePath/sst` by default.
- For production, it's common to place WAL on a fast, durable device (e.g., separate NVMe or RAID) and SSTs on larger, possibly slower storage.
- Consider configuring exporters and sampling for OpenTelemetry in the host composition when enabling telemetry.