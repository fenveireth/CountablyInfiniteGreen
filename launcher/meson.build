project('launcher', 'c',
  version : '1',
  default_options : ['c_args=-municode', 'warning_level=2'])

sh = find_program('sh')

loader = library('loader', 'loader.c')

embedded_objs = [
  custom_target('loaderobj',
    depends: loader,
    output: 'libloader.o',
    command:[sh, '-c', 'cp ' + loader.full_path() + ' libloader && ld -r -b binary -o libloader.o libloader'])
]

embedded_files = ['FenLoader', '0Harmony', 'Mono.Cecil', 'MonoMod.RuntimeDetour', 'MonoMod.Utils']
foreach e : embedded_files
  obj = custom_target(e,
    input: '../FenLoader/bin/Release/net4.8/' + e + '.dll',
    output: e + '.o',
    command:[sh, '-c', 'cp @INPUT@ ' + e + ' && ld -r -b binary -o ' + e + '.o ' + e])
  embedded_objs += obj
endforeach

executable('goldentreasure_loader', 'launcher.c', embedded_objs, win_subsystem:'windows')

