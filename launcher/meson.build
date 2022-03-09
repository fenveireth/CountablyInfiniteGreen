project('launcher', 'c',
  version : '1',
  default_options : ['c_args=-municode', 'warning_level=2'])
windows = import('windows')

loader = library('loader', 'loader.c')

embedded_files = [
  'launcher.ico',
  '../FenLoader/bin/Release/net4.8/FenLoader.dll',
  '../FenLoader/bin/Release/net4.8/0Harmony.dll',
  '../FenLoader/bin/Release/net4.8/Mono.Cecil.dll',
  '../FenLoader/bin/Release/net4.8/MonoMod.RuntimeDetour.dll',
  '../FenLoader/bin/Release/net4.8/MonoMod.Utils.dll',
]

embedded_objs = [
  windows.compile_resources('launcher.rc', depend_files:embedded_files, depends:loader),
]

executable('goldentreasure_loader', 'launcher.c', embedded_objs, win_subsystem:'windows')
