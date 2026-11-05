#!/bin/bash
# Builds BatteryWidget.exe (.NET Framework 4.8 – built into Windows 10 and 11) with the sensor library packed inside.
set -e
cd "$(dirname "$0")"
RES=""
for f in lhm/*.dll; do RES="$RES -resource:$f,lhm.$(basename $f)"; done
WIN32RES=""; [ -f res/app.res ] && WIN32RES="-win32res:res/app.res"
mcs -sdk:4.8 -langversion:experimental -target:winexe -platform:anycpu -optimize+ -out:${1:-out/BatteryWidget.exe} $WIN32RES \
  -r:System.Web.Extensions.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Drawing.dll \
  -r:System.Windows.Forms.dll -r:System.Net.Http.dll -r:lhm/LibreHardwareMonitorLib.dll $RES src/*.cs 2>&1 | grep -E "error|succeeded|failed"
