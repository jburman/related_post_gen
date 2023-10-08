using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;


const int topN = 5;
var posts = JsonSerializer.Deserialize(File.ReadAllText(@"../posts.json"), MyJsonContext.Default.ListPost)!;
var postsCount = posts.Count;

var sw = Stopwatch.StartNew();

// Compute a bitmask that represents each tag that is used
// TODO: this approach only works up until there are 30 unique tags
Dictionary<string, int> tagIndexMap = new();
uint[] postTagMasks = new uint[postsCount];
int tagIndex = 0;

for (var i = 0; i < postsCount; i++)
{
    foreach (var tag in posts[i].Tags)
    {
        ref int index = ref CollectionsMarshal.GetValueRefOrAddDefault(tagIndexMap, tag, out bool exists);
        if (!exists)
        {
            index = ++tagIndex;
        }
        postTagMasks[i] |= (uint)(1 << index);
    }
}

Span<RelatedPosts> allRelatedPosts = new RelatedPosts[postsCount];
Span<(byte Count, int PostId)> top5 = stackalloc (byte Count, int PostId)[topN];

for (var i = 0; i < postsCount; i++)
{
    uint tagMask = postTagMasks[i];
    top5.Clear();
    byte minTags = 0;

    // custom priority queue to find top N
    // two loops are used to avoid post including its own index
    for (var j = 0; j < i; j++)
    {
        // us population count to compare how many tags are in common
        byte count = (byte)BitOperations.PopCount(tagMask & postTagMasks[j]);

        if (count > minTags)
        {
            int upperBound = topN - 2;

            while (upperBound >= 0 && count > top5[upperBound].Count)
            {
                top5[upperBound + 1] = top5[upperBound];
                upperBound--;
            }

            top5[upperBound + 1] = (count, j);

            minTags = top5[topN - 1].Count;
        }
    }
    
    for (var j = i+1; j < postsCount; j++)
    {
        byte count = (byte)BitOperations.PopCount(tagMask & postTagMasks[j]);

        if (count > minTags)
        {
            int upperBound = topN - 2;

            while (upperBound >= 0 && count > top5[upperBound].Count)
            {
                top5[upperBound + 1] = top5[upperBound];
                upperBound--;
            }

            top5[upperBound + 1] = (count, j);

            minTags = top5[topN - 1].Count;
        }
    }

    var topPosts = new Post[topN];

    // Convert indexes back to Post references. skip even indexes
    for (int j = 0; j < 5; j ++)
    {
        topPosts[j] = posts[top5[j].PostId];
    }

    allRelatedPosts[i] = new RelatedPosts
    {
        Id = posts[i].Id,
        Tags = posts[i].Tags,
        Related = topPosts
    };
}

sw.Stop();

Console.WriteLine($"Processing time (w/o IO): {sw.Elapsed.TotalMilliseconds}ms");

File.WriteAllText(@"../related_posts_csharp.json", JsonSerializer.Serialize(allRelatedPosts.ToArray(), MyJsonContext.Default.RelatedPostsArray));

public record struct Post
{
    [JsonPropertyName("_id")]
    public string Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; }
}

public record RelatedPosts
{
    [JsonPropertyName("_id")]
    public string Id { get; set; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; }

    [JsonPropertyName("related")]
    public Post[] Related { get; set; }
}

[JsonSerializable(typeof(Post))]
[JsonSerializable(typeof(List<Post>))]
[JsonSerializable(typeof(RelatedPosts))]
[JsonSerializable(typeof(RelatedPosts[]))]
public partial class MyJsonContext : JsonSerializerContext { }
