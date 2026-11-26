using Godot;
using static EdenDeck.App.Ui.Poly.Ps1;

namespace EdenDeck.App.Ui.Poly;

/// <summary>
/// Bought low-poly models (Lowbit Lab packs) for the 3D theme's props. Their licence allows
/// shipping them inside a built app but not redistributing the files, so they are not in this
/// repository: the release workflow copies them from a private repo into res://Licensed/ before
/// export. Without them (source builds, forks) every prop falls back to its generated version.
/// Each model has a root node named after the prop with "body" and named moving parts as
/// children; a part node sits at its pivot, so turning it turns the part about that point.
/// </summary>
public static class Models
{
    private const string Dir = "res://Licensed/";

    private static bool? _available;

    /// <summary>True when the licensed models were baked into this build.</summary>
    public static bool Available => _available ??= ResourceLoader.Exists(Dir + "tv.glb");

    public static bool Has(string name) => Available && ResourceLoader.Exists($"{Dir}{name}.glb");

    /// <summary>
    /// Instantiates a model under <paramref name="parent"/>, swapping its materials for the PS1
    /// look's (vertex snap, affine textures, fog) and keeping each part's materials at hand.
    /// </summary>
    public static Model Load(Look l, string name, Node3D parent)
    {
        var scene = GD.Load<PackedScene>($"{Dir}{name}.glb").Instantiate<Node3D>();
        parent.AddChild(scene);
        Texture2D? glow = ResourceLoader.Exists($"{Dir}{name}_glow.png") ? GD.Load<Texture2D>($"{Dir}{name}_glow.png") : null;
        var m = new Model(scene, scene.FindChild(name, true, false) as Node3D ?? scene);
        // The glTF importer turns each part node into a MeshInstance3D of the same name.
        foreach (var mi in Meshes(scene))
        {
            var key = mi.Name.ToString();
            for (var i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
            {
                var src = mi.Mesh.SurfaceGetMaterial(i) as BaseMaterial3D;
                var mat = l.Mat(src?.AlbedoColor ?? Colors.White, src?.AlbedoTexture);
                if (glow != null)
                {
                    mat.Param("glow_tex", glow);
                }
                mi.SetSurfaceOverrideMaterial(i, mat);
                m.Add(key, mat);
            }
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        return m;
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D mi)
        {
            yield return mi;
        }
        foreach (var c in n.GetChildren())
        {
            foreach (var x in Meshes(c))
            {
                yield return x;
            }
        }
    }
}

/// <summary>An instantiated bought model: its root and its parts' nodes and materials.</summary>
public sealed class Model(Node3D scene, Node3D root)
{
    private readonly Dictionary<string, List<ShaderMaterial>> _mats = new();
    private readonly Dictionary<string, Quaternion> _rest = new();

    public Node3D Scene { get; } = scene;
    public Node3D Root { get; } = root;

    public void Add(string part, ShaderMaterial m)
    {
        if (!_mats.TryGetValue(part, out var list))
        {
            _mats[part] = list = new();
        }
        list.Add(m);
    }

    public Node3D Part(string name) => Root.FindChild(name, true, false) as Node3D
        ?? throw new InvalidOperationException($"model {Root.Name} has no part {name}");

    /// <summary>The materials of one part (or every part, with no name).</summary>
    public IEnumerable<ShaderMaterial> Mats(string? part = null) =>
        part == null ? _mats.Values.SelectMany(x => x) : _mats.GetValueOrDefault(part) ?? [];

    public void Param(string? part, StringName param, Variant value)
    {
        foreach (var m in Mats(part))
        {
            m.Param(param, value);
        }
    }

    /// <summary>Turns a part about its own local axis on top of the rotation it was built with.</summary>
    public void Turn(Node3D part, Vector3 axis, float degrees)
    {
        var key = part.Name.ToString();
        if (!_rest.TryGetValue(key, out var rest))
        {
            _rest[key] = rest = part.Quaternion;
        }
        part.Quaternion = rest * new Quaternion(axis.Normalized(), Mathf.DegToRad(degrees));
    }
}
