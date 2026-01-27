# Debugging Setup for .NET Framework Project

## The Problem
VS Code has **limited support** for debugging .NET Framework projects. The standard C# extension doesn't support .NET Framework debugging.

## Solutions

### Option 1: Install C# Extension (Recommended First Step)
1. Open VS Code Extensions (Ctrl+Shift+X)
2. Search for: **"C#"** (by Microsoft, ID: `ms-dotnettools.csharp`)
3. Install it
4. This provides basic IntelliSense and build support

### Option 2: Try to Find C# Dev Kit
The C# Dev Kit provides .NET Framework debugging support:
1. Open Extensions (Ctrl+Shift+X)
2. Search for: **"C# Dev Kit"** or **"csdevkit"**
3. If found, install it (may require Microsoft account)
4. Extension ID: `ms-dotnettools.csdevkit`

**If you can't find it:**
- It might require a Visual Studio subscription
- Try searching in the marketplace: https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit
- Install directly from the marketplace link

### Option 3: Use Visual Studio (Best for .NET Framework)
Visual Studio has full .NET Framework debugging support:
1. Download Visual Studio Community (free)
2. Open the `.sln` file in Visual Studio
3. Press F5 to debug

### Option 4: Run Without Full Debugging
The current configuration will at least **run** your app:
- Press F5 to build and run
- Breakpoints won't work, but you can see console output
- Use `Console.WriteLine()` for debugging

## Current Configuration
The `launch.json` is set up to run your executable, but full debugging (breakpoints, variable inspection) requires C# Dev Kit or Visual Studio.
