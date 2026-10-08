# 第三方组件及许可

本文件列出工具包安装器随附的第三方组件。`legacy/`、`previous/` 内的旧版安装器同样包含下列 Mono.Cecil 组件，其许可一并适用。

## Mono.Cecil 0.11.6

安装器的单文件包包含 `Mono.Cecil.dll`、`Mono.Cecil.Mdb.dll`、`Mono.Cecil.Pdb.dll` 和 `Mono.Cecil.Rocks.dll`，用于读取、检查和修改用户本机的 .NET 程序集。它们是程序集处理库，不是游戏文件。

项目：[jbevain/cecil](https://github.com/jbevain/cecil)

版本：[Mono.Cecil 0.11.6](https://www.nuget.org/packages/Mono.Cecil/0.11.6)

许可：MIT；以下版权及许可全文来自该版本的 [LICENSE.txt](https://github.com/jbevain/cecil/blob/0.11.6/LICENSE.txt)。本地 NuGet 包的许可声明亦为 MIT。

```text
Copyright (c) 2008 - 2015 Jb Evain
Copyright (c) 2008 - 2011 Novell, Inc.

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Microsoft .NET 应用启动宿主

`AdvisorSetup.exe` 使用 Microsoft .NET 的 Windows x64 应用启动宿主（apphost）包装为单文件程序。当前构建使用 `Microsoft.NETCore.App.Host.win-x64` 9.0.17；该组件的 [官方包许可](https://www.nuget.org/packages/Microsoft.NETCore.App.Host.win-x64/9.0.17) 为 MIT。

安装器目标为 `net9.0`，以 `SelfContained=false` 发布。工具包包含应用启动宿主、安装器代码、配置和上文的 Mono.Cecil 库，**不附带完整 .NET 运行时**。使用安装器需要另行安装 .NET 9 x64 运行时，或包含此运行时的 .NET SDK。

项目：[dotnet/runtime](https://github.com/dotnet/runtime)

包版权声明：© Microsoft Corporation. All rights reserved.

以下为该版本的 [LICENSE.TXT](https://github.com/dotnet/runtime/blob/v9.0.17/LICENSE.TXT) 全文。完整 .NET Runtime 的其他组件通知见其 [THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/runtime/blob/v9.0.17/THIRD-PARTY-NOTICES.TXT)；本工具包不分发该完整运行时。

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

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
