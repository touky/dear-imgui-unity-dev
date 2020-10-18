@ECHO off
echo Starting by copying dlls: Ensure unity is closed
PAUSE

ROBOCOPY .\ImGui.NET\deps\cimgui\ ..\..\..\dear-imgui-touky\Plugins\ /S /IS /XD win-x86

echo Copying Wrapper code
PAUSE

ROBOCOPY .\ImGui.NET\src\ImGui.NET\ ..\..\..\dear-imgui-touky\ImGuiNET\Wrapper\ *.cs /S /IS 

echo Patching code

FORFILES /P ..\..\..\dear-imgui-touky\ImGuiNET\Wrapper /S /M *.cs /C "Cmd /C ReplaceText @path System.Numerics; UnityEngine;"

echo Copying imgui_demo
PAUSE

COPY /y .\imgui\imgui_demo.cpp ..\..\..\dear-imgui-touky\imgui_demo.cpp.txt

ECHO Copy is done!
PAUSE