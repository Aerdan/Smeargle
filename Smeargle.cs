
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using Tommy;

namespace Smeargle {
	class Game {
		public string Name { get; }
		public Dictionary<string, Font> Fonts { get; }
		public List<Script> Scripts { get; }

		public Game(TomlTable config) {
			Name = config["name"];
			Fonts = new Dictionary<string, Font>();
			Scripts = new List<Script>();

			foreach (string key in config["font"].Keys) {
				TomlTable font_table;
				Font font;
				using (StreamReader reader = new StreamReader(File.OpenRead(config["font"][key]))) {
					try {
						font_table = TOML.Parse(reader);
						font = new Font(font_table);
						Fonts[font.Name] = font;
					} catch (TomlParseException ex) {
						Console.WriteLine($"Error parsing {config["font"][key]}:");
						foreach (TomlSyntaxException synex in ex.SyntaxErrors) {
							Console.WriteLine($"{synex.Line}:{synex.Column}: {synex.Message}");
						}
						System.Environment.Exit(1);
					}
				}
			}

			foreach (string key in config["script"].Keys) {
				Script script = new Script(config["script"][key].AsTable, this);
				Scripts.Add(script);
			}
		}
		private void CreateDirectoryForFile(string filename) {
			int index = filename.LastIndexOf('/');
			string directory;

			if (index == -1) {
				return;
			}
			directory = filename.Substring(0, index);
			if (!Directory.Exists(directory)) {
				Directory.CreateDirectory(filename.Substring(0, index));
			}
		}
		public void RenderScript(Script script, bool output=true) {
			List<Line> lines;
			if (output) { Console.WriteLine("Rendering text..."); }
			lines = script.RenderLines();
			if (output) { Console.WriteLine("Text rendered."); }

			if (output) { Console.Write("Generating tilemap..."); }
			Tilemap tilemap = script.GenerateTilemap(lines);
			if (output) { Console.WriteLine($"{tilemap.Tiles} tiles generated, {tilemap.UniqueTiles} unique."); }

			if (output) { Console.WriteLine("Writing compressed tiles..."); }
			CreateDirectoryForFile(script.DedupeFilename);
			script.RenderTilesToFile(tilemap.Compressed, script.DedupeFilename);
			if (output) { Console.WriteLine("Writing raw tiles..."); }
			CreateDirectoryForFile(script.RawFilename);
			script.RenderTilesToFile(tilemap.Raw, script.RawFilename);

			if (output) { Console.WriteLine("Writing map indices..."); }
			CreateDirectoryForFile(script.TilemapFilename);
			using (StreamWriter writer = new StreamWriter(File.OpenWrite(script.TilemapFilename))) {
				foreach (Tuple<Line, string> line in tilemap.Indices) {
					if (script.OutputFormat == "thingy") {
						writer.WriteLine($"{line.Item2}={line.Item1.Text}");
					}
					else {
						writer.WriteLine($"{line.Item1.Text} = {line.Item2}");
					}
				}
			}
			if (output) {
				Console.WriteLine($"\nRaw tiles:        {script.RawFilename}");
				Console.WriteLine($"Compressed tiles: {script.DedupeFilename}");
				Console.WriteLine($"Tile<->text:      {script.TilemapFilename}");
			}
		}
	}
	class Font {
		public string Name { get; }
		private string Filename;
		public int Bits { get; }
		public int Width { get; }
		public int Height { get; }
		public ColorPalette Palette { get; }
		public Dictionary<string, Tuple<int, int>> Map { get; }
		private Bitmap Image;

		public Font(TomlTable config) {
			Name     = config["name"];
			Filename = config["filename"];
			Bits     = config["bits_per_pixel"].AsInteger;
			Width    = config["width"].AsInteger;
			Height   = config["height"].AsInteger;

			Map = new Dictionary<string, Tuple<int, int>>();
			foreach (string key in config["map"].Keys) {
				int width = config["map"][key][1].AsInteger;
				int index = config["map"][key][0].AsInteger;
				Tuple<int, int> data = new Tuple<int, int>(index, width);
				Map[key] = data;
			}

			Image = new Bitmap(Filename);
			if (Image.PixelFormat == PixelFormat.Indexed) {
				Palette = Image.Palette;
			} else {
				Console.WriteLine($"WARNING: {Filename} is not color-indexed; output will be unpredictable.");
			}
		}

		public Bitmap Index(int index) {
			int tile_width = Image.Width / Width;
			int row = (index - 1) / tile_width;
			int col = (index - 1) % tile_width;

			int x = col * Width;
			int y = row * Height;

			if ((x > Image.Width) || (y > Image.Height)) {
				throw new IndexOutOfRangeException("Index was outside the bounds of the font.");
			}
			return Image.Clone(new Rectangle(x, y, Width, Height), PixelFormat.DontCare);
		}
		public int Length(string text) {
			int ret = 0;
			foreach (char character in text) {
				ret += Map[character.ToString()].Item2;
			}
			return ret;
		}
	}
	struct Line {
		public string Text { get; }
		public Bitmap Image { get; }
		public int Length { get; }
		public Line(string text, Bitmap image, int length) {
			Text = text;
			Image = image;
			Length = length;
		}
	}
	struct Tilemap {
		public Dictionary<Color[], Bitmap> Map;
		public List<Bitmap> Raw;
		public List<Bitmap> Compressed;
		public Dictionary<Color[], string> MapIndices;
		public List<Tuple<Line,string>> Indices;
		public int Tiles;
		public int UniqueTiles;

		public Tilemap(Dictionary<Color[], Bitmap> tilemap, List<Bitmap> raw, List<Bitmap> compressed, Dictionary<Color[], string> map_indices, List<Tuple<Line, string>> indices, int tiles, int unique) {
			Map = tilemap;
			Raw = raw;
			Compressed = compressed;
			MapIndices = map_indices;
			Indices = indices;
			Tiles = tiles;
			UniqueTiles = unique;
		}
	}
	class Script {
		private string Filename;
		private Font Font;
		private int MinimumTilesPerLine;
		private int MaximumTilesPerLine;
		public string OutputFormat { get; }
		private bool LeadingZeroes;
		private int TileOffset;
		private bool LittleEndian;
		public string RawFilename { get; }
		public string DedupeFilename { get; }
		public string TilemapFilename { get; }
		private string[] Text;

		private int GetOrDefault(TomlTable table, string key, int def) {
			if (table.Keys.Contains(key)) {
				return table[key];
			}
			return def;
		}
		private string GetOrDefault(TomlTable table, string key, string def) {
			if (table.Keys.Contains(key)) {
				return table[key];
			}
			return def;
		}
		private bool GetOrDefault(TomlTable table, string key, bool def) {
			if (table.Keys.Contains(key)) {
				return table[key];
			}
			return def;
		}

		public Script(TomlTable config, Game game) {
			Filename            = config["filename"];
			Font                = game.Fonts[config["font"]];
			MinimumTilesPerLine = GetOrDefault(config, "min_tiles_per_line", 0);
			MaximumTilesPerLine = GetOrDefault(config, "max_tiles_per_line", 0);
			OutputFormat 		= GetOrDefault(config, "tilemap_format", null);
			LeadingZeroes       = GetOrDefault(config, "leading_zeroes", false);
			TileOffset          = GetOrDefault(config, "tile_offset", 0);
			LittleEndian        = GetOrDefault(config, "little_endian", false);
			RawFilename         = GetOrDefault(config, "raw_filename", $"{Filename} Data/raw.png");
			DedupeFilename      = GetOrDefault(config, "dedupe_filename", $"{Filename} Data/dedupe.png");
			TilemapFilename     = GetOrDefault(config, "tilemap_filename", $"{Filename} Data/tilemap.txt");

			using (StreamReader reader = new StreamReader(File.OpenRead(Filename))) {
				List<string> input = new List<string>();
				while (reader.Peek() >= 0) {
					input.Add(reader.ReadLine());
				}
				Text = input.ToArray();
			}
		}

		public List<Line> RenderLines() {
			Dictionary<string, Tuple<int, int>> map = this.Font.Map;
			int max_tiles = MaximumTilesPerLine * this.Font.Width;
			int min_tiles = MinimumTilesPerLine * this.Font.Width;
			List<Line> lines = new List<Line>();

			foreach (string line in Text) {
				if (line.Length < 1) {
					continue;
				}
				int length = (int)Math.Ceiling((double)Font.Length(line) / Font.Width) * Font.Width;
				int position = 0;

				if ((max_tiles > 0) && (max_tiles < length)) {
					Console.WriteLine($"WARNING: '{line}' exceeds {MaximumTilesPerLine} tiles by {length - max_tiles}px; truncating.");
					length = max_tiles;
				}
				if ((min_tiles > 0) && (length < min_tiles)) {
					Console.WriteLine($"INFO: '{line}' is shorter than {min_tiles} tiles by {min_tiles - length}px.");
					length = min_tiles;
				}
				Bitmap image = new Bitmap(length, Font.Height);
				Graphics painter = Graphics.FromImage(image);
				painter.Clear(Font.Index(Font.Map[" "].Item1).GetPixel(0, 0));

				foreach (char character in line) {
					Tuple<int,int> data = Font.Map[character.ToString()];
					int width = data.Item2;
					if (((position + width) > max_tiles) && (max_tiles > 0)) {
						break;
					}
					painter.DrawImage(Font.Index(data.Item1), position, 0);
					position += width;
				}
				lines.Add(new Line(line, image, length));
			}
			return lines;
		}
		public Tilemap GenerateTilemap(List<Line> lines) {
			Dictionary<Color[], Bitmap> tilemap = new Dictionary<Color[], Bitmap>();
			List<Bitmap> raw = new List<Bitmap>();
			List<Bitmap> compressed = new List<Bitmap>();
			Dictionary<Color[], string> map_indices = new Dictionary<Color[], string>();
			int unique = 0, total = 0;
			List<Tuple<Line, string>> indices = new List<Tuple<Line, string>>();

			foreach (Line line in lines) {
				List<string> tile_index = new List<string>();
				int tiles = line.Length / Font.Width;
				int column = 0;

				while (tiles > 0) {
					Bitmap tile = line.Image.Clone(new Rectangle(column, 0, Font.Width, Font.Height), PixelFormat.Indexed);
					Color[] hash = new Color[Font.Width * Font.Height];
					foreach (int y in Enumerable.Range(0, Font.Height)) {
						foreach (int x in Enumerable.Range(0, Font.Width)) {
							hash[y * Font.Height + x] = tile.GetPixel(x, y);
						}
					}

					if (!tilemap.Keys.Contains(hash)) {
						unique++;
						tilemap[hash] = tile;
						compressed.Add(tile);
						int index = unique + TileOffset;
						int upper = index / 256;
						int lower = index % 256;

						if ((upper > 0) || LeadingZeroes) {
							if (LittleEndian) {
								int temp = upper;
								upper = lower;
								lower = temp;
							}
							if (OutputFormat.Equals("atlas")) {
								map_indices[hash] = $"<${upper,2:x}><${lower,2:x}>";
							} else if (OutputFormat.Equals("thingy")) {
								map_indices[hash] = $"{upper,2:x}{lower,2:x}";
							} else {
								map_indices[hash] = $"0x{upper,2:x}{lower,2:x}";
							}
						} else {
							if (OutputFormat.Equals("atlas")) {
								map_indices[hash] = $"<${lower,2:x}>";
							} else if (OutputFormat.Equals("thingy")) {
								map_indices[hash] = $"{lower,2:x}";
							} else {
								map_indices[hash] = $"0x{lower,2:x}";
							}
						}
					}
					raw.Add(tile);
					tile_index.Add(map_indices[hash]);
					total++;
					column += Font.Width;
					tiles--;
				}

				if (OutputFormat == null) {
					indices.Add(new Tuple<Line, string>(line, String.Join(" ", tile_index)));
				} else{
					indices.Add(new Tuple<Line, string>(line, String.Join("", tile_index)));
				}
			}
			return new Tilemap(tilemap, raw, compressed, map_indices, indices, total, unique);
		}
		public Bitmap RenderTiles(List<Bitmap> tiles) {
			Bitmap image = new Bitmap(Font.Width * 16, (int)Math.Ceiling(tiles.Count / 16.0) * Font.Height);
			Graphics painter = Graphics.FromImage(image);
			painter.Clear(Font.Index(Font.Map[" "].Item1).GetPixel(0, 0));
			int row = 0, column = 0;

			foreach (Bitmap tile in tiles) {
				painter.DrawImage(tile, column, row);

				if (column < (Font.Width * 15)) {
					column += Font.Width;
				} else {
					column = 0;
					row += Font.Height;
				}
			}

			return image;
		}
		public void RenderTilesToFile(List<Bitmap> tiles, string filename) {
			RenderTiles(tiles).Save(filename);
		}
	}
	class Smeargle {
		static int Main(string[] args) {
			if (args.Length != 1) {
				Console.WriteLine("Usage: Smeargle.exe game.toml");
				return 1;
			}
			TomlTable table = null;
			Game game;
			using (StreamReader reader = new StreamReader(File.OpenRead(args[0]))) {
				try {
					table = TOML.Parse(reader);
				} catch (TomlParseException ex) {
					Console.WriteLine($"Error parsing {args[0]}:");
					foreach (TomlSyntaxException synex in ex.SyntaxErrors) {
						Console.WriteLine($"{synex.Line}:{synex.Column}: {synex.Message}");
					}
					return 1;
				}
			}
			game = new Game(table);
			foreach (Script script in game.Scripts) {
				game.RenderScript(script);
			}
			return 0;
		}
	}
}