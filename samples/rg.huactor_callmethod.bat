@echo off  
cd /d %~dp0
set cd0=%cd%
cd ../scripts/rgb
del log.*.log rg.info.json 1>nul 2>nul 
rgb
if not exist rg.info.json goto end
rmdir /s/q "%cd0%/obj/rg/huactor_callmethod" 1>nul 2>nul
copy /y rg.info.json "../rg/huactor_callmethod/rg.info.json"
cd ../rg/huactor_callmethod
del log.*.log 1>nul 2>nul 
rg --med_OnHandleCallMethod "__HuActorOnHandleCallMethod__" --med-not-in "OnLoad_Core;OnUnload_Core;__HuActorOnHandleCallMethod__" --ty-IHandleCallMethod "" --gen-ctor 1
:end
cd %cd0%
rmdir /s/q "../ConsoleApp1/obj/rg/huactor_callmethod" 1>nul 2>nul