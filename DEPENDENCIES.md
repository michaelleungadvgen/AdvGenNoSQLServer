# AdvGenNoSQL Server - Dependencies and License Compliance

**Project**: AdvGenNoSQL Server  
**Version**: 1.0.0  
**License**: MIT License  
**Last Updated**: March 25, 2026

---

## Overview

This document provides a comprehensive list of all dependencies used by AdvGenNoSQL Server, including their licenses and compatibility status with the MIT License.

## License Compatibility Policy

AdvGenNoSQL Server is released under the **MIT License**. All dependencies must be compatible with this license:

- ✓ **Permitted**: MIT, Apache 2.0, BSD (2/3-Clause), ISC
- ✗ **Prohibited**: GPL, AGPL, SSPL, proprietary/closed-source

---

## .NET Runtime Dependencies

| Package | Version | License | License URL | Compatibility | Notes |
|---------|---------|---------|-------------|---------------|-------|
| .NET Runtime | 9.0.x | MIT | https://github.com/dotnet/runtime/blob/main/LICENSE.TXT | ✓ Compatible | Core runtime |
| Microsoft.NETCore.App | 9.0.x | MIT | https://github.com/dotnet/core/blob/main/LICENSE.TXT | ✓ Compatible | Framework libraries |

---

## Microsoft Libraries (System.*)

All Microsoft System.* libraries are part of .NET and licensed under MIT:

| Namespace | Purpose | License | Compatibility |
|-----------|---------|---------|---------------|
| System.Security.Cryptography | Encryption, hashing, TLS | MIT | ✓ Compatible |
| System.Text.Json | JSON serialization | MIT | ✓ Compatible |
| System.Collections.Concurrent | Thread-safe collections | MIT | ✓ Compatible |
| System.IO.Pipelines | High-performance I/O | MIT | ✓ Compatible |
| System.Buffers | Buffer management | MIT | ✓ Compatible |
| System.Memory | Span<T>, Memory<T> | MIT | ✓ Compatible |
| System.Threading.Channels | Async channels | MIT | ✓ Compatible |
| System.Threading.Tasks.Dataflow | Dataflow blocks | MIT | ✓ Compatible |
| System.Net.Sockets | TCP networking | MIT | ✓ Compatible |
| System.Net.Security | SSL/TLS streams | MIT | ✓ Compatible |
| System.Diagnostics | Process diagnostics | MIT | ✓ Compatible |
| System.Linq | LINQ queries | MIT | ✓ Compatible |

---

## NuGet Package Dependencies

### Core Dependencies

| Package | Version | License | License URL | Compatibility | Purpose |
|---------|---------|---------|-------------|---------------|---------|
| Microsoft.Extensions.Hosting | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Host application framework |
| Microsoft.Extensions.Hosting.Abstractions | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Hosting abstractions |
| Microsoft.Extensions.DependencyInjection | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Dependency injection |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | DI abstractions |
| Microsoft.Extensions.Configuration | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Configuration management |
| Microsoft.Extensions.Configuration.Json | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | JSON configuration |
| Microsoft.Extensions.Configuration.FileExtensions | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | File-based config |
| Microsoft.Extensions.Logging | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Logging framework |
| Microsoft.Extensions.Logging.Console | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Console logging |
| Microsoft.Extensions.Logging.Debug | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Debug logging |
| Microsoft.Extensions.Options | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Options pattern |

### Third-Party Dependencies

| Package | Version | License | License URL | Compatibility | Purpose |
|---------|---------|---------|-------------|---------------|---------|
| Serilog | 3.0.1 | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | ✓ Compatible | Structured logging |
| Serilog.Sinks.Console | 4.1.0 | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | ✓ Compatible | Console sink for Serilog |
| Serilog.Sinks.File | 5.0.0 | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | ✓ Compatible | File sink for Serilog |

---

## Testing Dependencies

| Package | Version | License | License URL | Compatibility | Purpose |
|---------|---------|---------|-------------|---------------|---------|
| xUnit | 2.9.0 | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | ✓ Compatible | Unit testing framework |
| xUnit.assert | 2.9.0 | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | ✓ Compatible | Assertion library |
| xUnit.analyzers | 1.15.0 | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | ✓ Compatible | xUnit analyzers |
| xUnit.runner.visualstudio | 2.8.2 | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | ✓ Compatible | Visual Studio test runner |
| Microsoft.NET.Test.Sdk | 17.11.0 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Test SDK |
| coverlet.collector | 6.0.2 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Code coverage |
| Moq | 4.20.70 | BSD-3-Clause | https://licenses.nuget.org/BSD-3-Clause | ✓ Compatible | Mocking framework |
| FluentAssertions | 6.12.0 | Apache-2.0 | https://licenses.nuget.org/Apache-2.0 | ✓ Compatible | Fluent assertions |

---

## Benchmarking Dependencies

| Package | Version | License | License URL | Compatibility | Purpose |
|---------|---------|---------|-------------|---------------|---------|
| BenchmarkDotNet | 0.14.0 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Performance benchmarking |
| BenchmarkDotNet.Annotations | 0.14.0 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Benchmark annotations |
| CommandLineParser | 2.9.1 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | CLI argument parsing |
| Iced | 1.17.0 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | x86/x64 disassembler |
| Microsoft.CodeAnalysis.CSharp | 4.1.0 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Roslyn compilers |
| Microsoft.Diagnostics.Runtime | 2.2.332302 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | CLR diagnostics |
| Microsoft.Diagnostics.Tracing.TraceEvent | 3.0.8 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | ETW tracing |
| Microsoft.DotNet.PlatformAbstractions | 3.1.6 | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Platform abstractions |

---

## Web Admin Dependencies (Blazor WebAssembly)

| Package | Version | License | License URL | Compatibility | Purpose |
|---------|---------|---------|-------------|---------------|---------|
| Microsoft.AspNetCore.Components.WebAssembly | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Blazor WASM |
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 9.0.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | Dev server |
| MudBlazor | 6.x | MIT | https://licenses.nuget.org/MIT | ✓ Compatible | UI component library |

---

## License Compatibility Matrix

| License | Type | MIT Compatible | Notes |
|---------|------|----------------|-------|
| MIT | Permissive | ✓ Yes | Can be used freely |
| Apache-2.0 | Permissive | ✓ Yes | Patent protection included |
| BSD-2-Clause | Permissive | ✓ Yes | Simple permissive license |
| BSD-3-Clause | Permissive | ✓ Yes | Includes non-endorsement clause |
| ISC | Permissive | ✓ Yes | Functionally equivalent to BSD-2 |
| GPL-2.0 | Copyleft | ✗ No | Viral license - incompatible |
| GPL-3.0 | Copyleft | ✗ No | Viral license - incompatible |
| AGPL-3.0 | Copyleft | ✗ No | Server-side copyleft - incompatible |
| SSPL | Source-available | ✗ No | MongoDB license - incompatible |
| Proprietary | Commercial | ✗ No | Closed source - incompatible |

---

## Excluded Dependencies (License Incompatibility)

The following dependencies are **explicitly excluded** from use due to license incompatibility:

| Package | License | Reason for Exclusion |
|---------|---------|---------------------|
| MongoDB.Driver | SSPL | Server Side Public License - incompatible with MIT |
| Entity Framework Core (certain extensions) | GPL variants | Copyleft restrictions |
| MySQL Connector (GPL variant) | GPL-2.0 | Copyleft - incompatible |
| Any GPL/AGPL libraries | Copyleft | Viral license terms |
| Closed-source/proprietary libraries | Proprietary | Source not available |

---

## Custom Implementation Priority

Where third-party libraries have restrictive licenses, we implement custom solutions:

| Feature | Custom Implementation | Reason |
|---------|----------------------|--------|
| Document Serialization | Custom JSON serializer using System.Text.Json | Avoid GPL serializers |
| Query Engine | Custom query processor | Avoid GPL query engines |
| Transaction Management | Custom 2PC implementation | Full control over ACID |
| Encryption Utilities | System.Security.Cryptography wrappers | No external crypto libs |
| JWT Handling | Custom JWT using HMAC-SHA256 | Avoid external JWT libraries |
| B-Tree Index | Custom implementation | Optimized for document store |

---

## Dependency Verification Commands

To verify dependency licenses in the project:

```powershell
# List all package references
dotnet list package --include-transitive

# Check for deprecated packages
dotnet list package --deprecated

# Check for vulnerable packages
dotnet list package --vulnerable
```

---

## Adding New Dependencies

Before adding any new NuGet package:

1. **Check License**: Verify at https://licenses.nuget.org/
2. **Verify Compatibility**: Ensure MIT-compatible (no GPL/AGPL/SSPL)
3. **Review Source**: Check source code repository for hidden dependencies
4. **Get Approval**: Document in this file if license is ambiguous
5. **Update This File**: Add to the appropriate section above

### Dependency Approval Checklist

- [ ] License verified as MIT-compatible
- [ ] No transitive dependencies with incompatible licenses
- [ ] Source code repository reviewed
- [ ] Added to DEPENDENCIES.md
- [ ] Security audit completed (if applicable)

---

## Transitive Dependency Audit

All transitive dependencies have been audited for license compatibility. The following transitive dependencies are included:

### From BenchmarkDotNet
- Perfolizer (MIT) - Statistical analysis
- System.Management (MIT) - WMI access
- System.CodeDom (MIT) - CodeDOM support

### From Microsoft.Extensions.*
- System.ComponentModel.Annotations (MIT)
- System.Diagnostics.DiagnosticSource (MIT)
- System.Text.Encodings.Web (MIT)
- System.Text.Json (MIT)

All transitive dependencies maintain MIT license compatibility.

---

## Legal Notice

This project is licensed under the MIT License:

```
MIT License

Copyright (c) 2026 AdvanGeneration Pty. Ltd.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Attribution

This software includes code from the following projects:

- **.NET Runtime** - Copyright © Microsoft Corporation. Licensed under MIT.
- **Serilog** - Copyright © Serilog Contributors. Licensed under Apache-2.0.
- **xUnit** - Copyright © xUnit.net Contributors. Licensed under Apache-2.0.
- **Moq** - Copyright © Moq Contributors. Licensed under BSD-3-Clause.
- **BenchmarkDotNet** - Copyright © .NET Foundation. Licensed under MIT.
- **MudBlazor** - Copyright © MudBlazor Contributors. Licensed under MIT.

---

## Updates

| Date | Author | Changes |
|------|--------|---------|
| 2026-03-25 | Agent-101 | Initial dependency documentation |

---

**Note**: This document should be reviewed and updated whenever new dependencies are added or existing dependencies are updated.
