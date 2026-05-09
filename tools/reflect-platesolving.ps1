$dll = "C:\Users\carls\.nuget\packages\nina.platesolving\3.2.0.9001\lib\net8.0-windows7.0\NINA.PlateSolving.dll"
$a = [Reflection.Assembly]::LoadFrom($dll)
$a.GetExportedTypes() | Where-Object { $_.FullName -match 'Plate|Solve|Blind|Mediator|Instruction' } | ForEach-Object { $_.FullName }
