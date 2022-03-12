# Visual effects

Introduces the tools availabe in the 'Backgrounds' xml files, for visual quality

See the examples in this folder in the demo mod

The 'Background' to a scene is what you choose with `\image{}`, so includes
everything visible beyond the word-board, even possibly interactible objects

## Layers

'Parallax' / layer float is automatically applied, as soon as your scene is
provided in separated layers (one transparent PNG each)

Layers are moved mechanically, as though they were stacked with equal space
between them

If you are trying to dial in an artwork that you've commissioned, it may be
worth getting back to the artist for source files : depending on how they work,
it may be possible for you to re-render them into layers with relatively little
Gimp-Fu

## Fade-ins

Several nodes can be added on an `<entity>` / layer, to make it artistically
fade in and out, move, shimmer, etc...

This is mostly used for aura and spirit-lines layers

Loop times are randomized, to make the scenes seem less mechnanical

### Pattern fades

The 'ShineLinear' component drives animation parameters, and the choice of
Material transforms those into a parametrized shape along which to fade

These patterns are mostly redundant, as similar results can be achieved with the
image-driven effects. I suggest you not worry about the resource use, and learn
only to use the latter

### Image-driven fade

'DissolveFade' operates with an extra fade texture, as a black-and-white image.  
Its dimensions are stretched to match the masked layer

The controller sweeps over the possible brightness values over time, down from
100% to 0%

Locations where the fade texture is brighter than the current reference are
considered to be faded in. Locations where it is darker are not faded in yet

These textures typically don't need to be very high-resolution. An extra noise
texture can optionally be provided, to roughen up the transitions if desired

## Displacement maps

'DistortionInitalize' operates with an extra displacement texture

On locations where the value is neutral (50% red, 50% green), the source layer
is drawn as usual.  
Lower/higher values for Red will offset the texture read from source to the
left/right  
Lower/higher values for Green will offset the texture read from source down/up

It also doubles up as a fade effect (similar but more limited than
'DissolveFade'), and has a UV scroll / loop / multiply on both input textures

This cannot plausibly simulate optics, but can be used to evocate water
reflection ripples, fluid flow, or heat shimmer
