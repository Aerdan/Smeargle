Smeargle Readme
---------------

Smeargle is a utility for prerendering text using image-based fonts. As
of this release, it is written in C# rather than Python, making it
easier than ever to use.

In order to use this program, you will need four things:

* A TOML file describing the game, fonts, and scripts used.
* A TOML file describing the individual fonts.
* A PNG or BMP file with the font in a gridded layout.
* A script in plaintext UTF-8 format, with no control codes.

TOML is a simple, but powerful, configuration file format; for more
information on the format, see https://github.com/toml-lang/toml .

Game TOML
---------
```
; This key is required, but exists for your reference.
name="Example Game"

; The following section is a mapping of font names to their TOML
; documents. It is required.
[font]
"Melissa 8"="melissa8.toml"

; One [script] section is required per script. The portion after the
; period is configurable. For example, it could be [script.test].
[script.example]
filename="example.txt"

; The value for this key must be a name specified in the [font]
; section.
font="Melissa 8"

; Number of tiles a line *must* have; a message is printed for lines
; shorter than this. Zero (0) means no minimum.
min_tiles_per_line=0

; Maximum number of tiles permitted in a line; a warning is printed and
; the line is truncated if this limit is exceeded. Zero (0) means no
; maximum.
max_tiles_per_line=0

; Tilemap format. This means a mapping is produced for use with Atlas
; or Thingy, or the default format if this key is omitted. Values are,
; naturally, "atlas" or "thingy".
tilemap_format="thingy"

; Whether each tile should be padded to 16-bit references or sized
; dynamically based on the number of unique tiles. 'False' here means
; dynamically sizing.
leading_zeroes=false

; This is a number added to the mapping so that the references start at
; a certain point.
tile_offset=0

; Whether 16-bit references in the tilemap should be in little-endian
; format or not.
little_endian=false

; The following are optional; if omitted, a directory will be created
; based on the script filename and the files will be placed there.
raw_filename="example_raw.png"
dedupe_filename="example_ded.png"
tilemap_filename="example.tbl"
```

Font TOML
---------
```
; Required, but only for your reference.
name="Melissa 8"
filename="melissa8.png"

; Currently unenforced and unused, but required for your reference.
bits_per_pixel=2

; Width of each cell in the font grid
width=8
; Height of each cell in the font grid
height=8

; Required section.
[map]
; Glyph, then a tuple of the index into the font and the width of the
; glyph in pixels. Note that this is 1-indexed. Note that a space (" ")
; reference must exist in order for Smeargle to backfill the output
; correctly.
" "=[115, 4]
```
