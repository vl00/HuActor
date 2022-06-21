@echo off  
cd /d %~dp0
set cd0=%cd%
cd ../scripts/rgb
del log.*.log rg.info.json 1>nul 2>nul 
rgb
if not exist rg.info.json goto end
rmdir /s/q "%cd0%/obj/rg/huactor_proxy" 1>nul 2>nul
copy /y rg.info.json "../rg/huactor_proxy/rg.info.json"
cd ../rg/huactor_proxy
del log.*.log 1>nul 2>nul 
rg
:end
cd %cd0%