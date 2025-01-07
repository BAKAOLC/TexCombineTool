using System.Collections.Concurrent;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

if (args.Length == 0)
{
    Console.WriteLine("Usage: TexCombineTool <input folder>");
    return;
}

var inputFolder = args[0];
if (string.IsNullOrWhiteSpace(inputFolder))
{
    Console.WriteLine("Input folder is empty");
    return;
}

if (!Directory.Exists(inputFolder))
{
    Console.WriteLine($"Input folder {inputFolder} not found");
    return;
}

var files = Directory.GetFiles(inputFolder, "*.png", SearchOption.TopDirectoryOnly);

var images = new ConcurrentBag<Sprite>();
var loadTasks = files.Select(file => Task.Run(async () =>
    {
        try
        {
            Console.WriteLine($"Load image {file}");
            var image = await Image.LoadAsync<Rgba32>(file);
            images.Add(new(Path.GetFileNameWithoutExtension(file), image));
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error loading image {file}: {e.Message}");
        }
    }))
    .ToArray();

await Task.WhenAll(loadTasks);

var fileName = Path.GetFileName(inputFolder);
await CreateTexture(fileName, 2, images.ToArray());

return;

async Task CreateTexture(string name, int margin, Sprite[] sprites)
{
    var size = SortSprite(sprites, margin);
    Console.WriteLine($"Create texture {name} in size {size}x{size}");
    var tex = new Image<Rgba32>(size, size);
    var sb = new StringBuilder();
    Console.WriteLine($"LoadTexture(\"{name}\", \"{name}.png\");");
    sb.AppendLine($"LoadTexture(\"{name}\", \"{name}.png\");");

    var x = 0;
    var y = 0;
    var mh = 0;
    var tasks = new List<Task>();
    foreach (var sprite in sprites)
    {
        if (x + sprite.Width + margin * 2 > size)
        {
            x = 0;
            y += mh;
            mh = 0;
        }

        mh = Math.Max(mh, sprite.Height);
        var point = new Point(x + margin, y + margin);
        Console.WriteLine($"LoadImage(\"{sprite.Name}\", \"{name}\", {point.X}, {point.Y}, {sprite.Width}, {sprite.Height});");
        sb.AppendLine($"LoadImage(\"{sprite.Name}\", \"{name}\", {point.X}, {point.Y}, {sprite.Width}, {sprite.Height});");
        tasks.Add(Task.Run(() => tex.Mutate(ctx => ctx.DrawImage(sprite.Graphic, point, 1f))));
        x += sprite.Width + margin * 2;
    }

    await Task.WhenAll(tasks);

    tasks.Clear();
    Console.WriteLine($"Save texture to {name}.png");
    tasks.Add(tex.SaveAsPngAsync($"{name}.png"));
    Console.WriteLine($"Save code to {name}.lua");
    tasks.Add(File.WriteAllTextAsync($"{name}.lua", sb.ToString()));

    await Task.WhenAll(tasks);
}

bool TryFillChunks(Sprite[] chunks, int margin, int total, int size)
{
    Console.WriteLine($"Try to fill chunks in size {size}x{size}");
    if (chunks.Length == 0)
    {
        return true;
    }

    if (size * size < total)
    {
        return false;
    }

    var calcChunks = new Rectangle[chunks.Length];
    for (var i = 0; i < chunks.Length; i++)
    {
        calcChunks[i] = new(0, 0, chunks[i].Width + margin * 2, chunks[i].Height + margin * 2);
    }

    var width = calcChunks.Max(x => x.Width + margin * 2);
    if (width > size)
    {
        return false;
    }

    var height = calcChunks.Max(x => x.Height + margin * 2);
    if (height > size)
    {
        return false;
    }

    var resultChunks = new List<Rectangle>();
    for (var i = 0; i < chunks.Length; i++)
    {
        var chunk = calcChunks[i];
        var @fixed = false;
        if (i == 0)
        {
            resultChunks.Add(new(0, 0, chunk.Width + margin * 2, chunk.Height + margin * 2));
            continue;
        }

        var yMax = 0;
        for (var y = 0; y < size - (chunk.Height + margin * 2); y++)
        {
            for (var x = 0; x < size - (chunk.Width + margin * 2); x++)
            {
                var rect = new Rectangle(x, y, chunk.Width + margin * 2, chunk.Height + margin * 2);
                var intersect = false;
                for (var j = 0; j < i; j++)
                {
                    if (!rect.IntersectsWith(resultChunks[j]))
                    {
                        continue;
                    }

                    intersect = true;
                    break;
                }

                if (intersect) continue;
                resultChunks.Add(rect);
                yMax = Math.Max(yMax, y);
                @fixed = true;
                break;
            }

            if (@fixed)
            {
                break;
            }
        }

        if (height <= size && width <= size && (i != chunks.Length - 1 || @fixed)) continue;
        return false;
    }

    return chunks.Length == resultChunks.Count;
}

int SortSprite(Sprite[] chunks, int margin)
{
    var total = chunks.Sum(x => (x.Width + margin * 2) * (x.Height + margin * 2));
    chunks = chunks.OrderByDescending(x => Math.Sqrt(x.Height)).ToArray();
    var size = 1;
    while (!TryFillChunks(chunks, margin, total, size))
    {
        size *= 2;
    }

    return size;
}

internal readonly struct Sprite(string name, Image<Rgba32> sprite)
{
    public readonly string Name = name;
    public readonly Image<Rgba32> Graphic = sprite;

    public int Width => Graphic.Width;

    public int Height => Graphic.Height;
}