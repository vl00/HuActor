@echo off  
cd /d %~dp0
set cd0=%cd%
cd ../scripts/rgb
del log.*.log rg.info.json 1>nul 2>nul 
rgb
if not exist rg.info.json goto end
rmdir /s/q "%cd0%/obj/rg/di_inject" 1>nul 2>nul
copy /y rg.info.json "../rg/di_inject/rg.info.json"
cd ../rg/di_inject
del log.*.log 1>nul 2>nul 
rg
:end
cd %cd0%