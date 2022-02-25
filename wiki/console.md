# In-game console reference

## Activation

As per tradition, a text prompt appears on pressing '`',
at the topleft on US-qwerty keyboard

Tradition also states you can go get fucked if you use azerty or other keymaps.
Just switch layouts temporarily

The prompt does nothing until it receives the right password, which is 'sphere'.
This does not persist across game restarts

Up arrow recalls last command

## Commands

### load &lt;event name&gt; &lt;transition&gt;
go to event. Transition defaults to "fade"

### savestate [&lt;slot&gt;]
untested, I've just used 'load' so far<br/>
Seems only meant to move you within a given scene<br/>
slot defaults to "debug_state"

### loadstate [&lt;slot&gt;]
slot defaults to "debug_state"

### set &lt;var&gt; &lt;value&gt;
sets variable

### set &lt;var&gt; &lt;value&gt; s
same as above, but force storing as text if value represents a number

### add &lt;var&gt; &lt;increment&gt;
increment can be name of another variable

### global &lt;var&gt; &lt;value&gt;
same as 'set', but writes to cross-lives save file

### message &lt;text&gt;
display text on the board. Use this with {} syntax to show variables

### effect &lt;name&gt; [&lt;channel&gt;]
play special effect or sound

### hunt &lt;prey&gt; [&lt;location&gt; [&lt;block retreat?&gt; [&lt;success location&gt;]]]
start hunt, location defaults to "HeartboneValleyBkg"

### fight &lt;prey&gt; [&lt;location&gt; [&lt;block retreat?&gt; [&lt;success location&gt;]]]
same as above, skip to fight

### chase &lt;prey&gt; [&lt;location&gt; [&lt;block retreat?&gt; [&lt;success location&gt;]]]
same as above, skip to chase

### addalltreasures
and research them all too

### clearalldata
profile wipe
