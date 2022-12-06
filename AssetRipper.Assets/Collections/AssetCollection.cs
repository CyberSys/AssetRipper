﻿using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Metadata;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.VersionUtilities;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AssetRipper.Assets.Collections;

/// <summary>
/// A collection of <see cref="IUnityObjectBase"/> assets.
/// </summary>
public abstract class AssetCollection : IReadOnlyCollection<IUnityObjectBase>, IAssetContainer
{
	protected AssetCollection(Bundle bundle)
	{
		dependencies.Add(this);
		Bundle = bundle;
		bundle.AddCollection(this);
	}

	public Bundle Bundle { get; }
	public string Name { get; protected set; } = string.Empty;
	public string FilePath { get; set; } = string.Empty;
	/// <summary>
	/// The list of dependencies for this collection.
	/// </summary>
	/// <remarks>
	/// The zeroth entry is <see langword="this"/> for correct correspondence with file indices.
	/// Entries are null if they could not be found.
	/// </remarks>
	public IReadOnlyList<AssetCollection?> Dependencies => dependencies;
	private readonly List<AssetCollection?> dependencies = new();
	public IReadOnlyDictionary<long, IUnityObjectBase> Assets => assets;
	private readonly Dictionary<long, IUnityObjectBase> assets = new();
	public UnityVersion Version { get; protected set; }
	public BuildTarget Platform { get; protected set; }
	public TransferInstructionFlags Flags { get; protected set; }
	public EndianType EndianType { get; protected set; }
	/// <summary>
	/// The GUID of this collection. If this is a scene, it gets used in the scene's meta file.
	/// </summary>
	public UnityGUID GUID { get; set; }
	/// <summary>
	/// The relative path of the scene without any file extension.
	/// </summary>
	/// <remarks>
	/// If the collection is not a scene, this represents nothing.
	/// </remarks>
	public string ScenePath
	{
		get => sceneName ?? $"Assets/Scenes/{Name}";
		set => sceneName = value;
	}
	private string? sceneName;
	
	public bool IsScene { get; set; }

	public int AddDependency(AssetCollection dependency)
	{
		int index = dependencies.IndexOf(dependency);
		if (index >= 0)
		{
			return index;
		}
		else if (IsCompatibleDependency(dependency))
		{
			dependencies.Add(dependency);
			return dependencies.Count - 1;
		}
		else
		{
			throw new ArgumentException($"Dependency is not compatible with this {nameof(AssetCollection)}.", nameof(dependency));
		}
	}

	protected void SetDependency(int index, AssetCollection? collection)
	{
		if (index < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}
		else if (index < dependencies.Count)
		{
			dependencies[index] = collection;
		}
		else
		{
			while (dependencies.Count < index)
			{
				dependencies.Add(null);
			}
			dependencies.Add(collection);
		}
	}

	protected virtual bool IsCompatibleDependency(AssetCollection dependency) => true;

	public PPtr<T> CreatePPtr<T>(T? asset) where T : IUnityObjectBase
	{
		if (asset is null)
		{
			return default;
		}

		int fileIndex = dependencies.IndexOf(asset.Collection);
		if (fileIndex < 0)
		{
			throw new Exception($"Asset doesn't belong to this {nameof(AssetCollection)} or any of its dependencies");
		}
		return new PPtr<T>(fileIndex, asset.PathID);
	}

	public PPtr<T> ForceCreatePPtr<T>(T? asset) where T : IUnityObjectBase
	{
		if (asset is null)
		{
			return default;
		}

		int fileIndex = AddDependency(asset.Collection);
		return new PPtr<T>(fileIndex, asset.PathID);
	}

	private protected void AddAsset(IUnityObjectBase asset)
	{
		Debug.Assert(asset.Collection == this, "Asset info must marked this as its collection.");
		assets.Add(asset.PathID, asset);
	}

	public override string ToString()
	{
		return Name;
	}

	#region GetAsset Methods
	public IUnityObjectBase GetAsset(long pathID)
	{
		return TryGetAsset(pathID, out IUnityObjectBase? asset)
			? asset
			: throw new ArgumentException($"Object with path ID {pathID} wasn't found.", nameof(pathID));
	}

	public T GetAsset<T>(long pathID) where T : IUnityObjectBase
	{
		IUnityObjectBase asset = GetAsset(pathID);
		return asset is T castedAsset
			? castedAsset
			: throw new ArgumentException($"Object with type {asset.GetType()} could not be assigned to type {typeof(T)}.", nameof(T));
	}

	public IUnityObjectBase? TryGetAsset(long pathID)
	{
		TryGetAsset(pathID, out IUnityObjectBase? asset);
		return asset;
	}

	public T? TryGetAsset<T>(long pathID) where T : IUnityObjectBase
	{
		TryGetAsset(pathID, out T? asset);
		return asset;
	}

	public bool TryGetAsset(long pathID, [NotNullWhen(true)] out IUnityObjectBase? asset)
	{
		if (pathID is 0)
		{
			asset = default;
			return false;
		}

		return assets.TryGetValue(pathID, out asset);
	}

	public bool TryGetAsset<T>(long pathID, [NotNullWhen(true)] out T? asset) where T : IUnityObjectBase
	{
		IUnityObjectBase? @object = TryGetAsset(pathID);
		switch (@object)
		{
			case null:
				asset = default;
				return false;
			case T t:
				asset = t;
				return true;
			case NullObject:
				asset = default;
				return false;
			default:
				throw new Exception($"Object's type {@object.GetType().Name} isn't assignable from {typeof(T).Name}");
		}
	}

	public IUnityObjectBase GetAsset(int fileIndex, long pathID)
	{
		ThrowIfFileIndexOutOfRange(fileIndex);
		AssetCollection file = Dependencies[fileIndex] ?? throw new ArgumentException($"Dependency collection with index {fileIndex} was not found.", nameof(fileIndex));
		return file.GetAsset(pathID);
	}

	public T GetAsset<T>(int fileIndex, long pathID) where T : IUnityObjectBase
	{
		ThrowIfFileIndexOutOfRange(fileIndex);
		AssetCollection file = Dependencies[fileIndex] ?? throw new ArgumentException($"Dependency collection with index {fileIndex} was not found.", nameof(fileIndex));
		return file.GetAsset<T>(pathID);
	}

	public IUnityObjectBase? TryGetAsset(int fileIndex, long pathID)
	{
		TryGetAsset(fileIndex, pathID, out IUnityObjectBase? asset);
		return asset;
	}

	public T? TryGetAsset<T>(int fileIndex, long pathID) where T : IUnityObjectBase
	{
		TryGetAsset(fileIndex, pathID, out T? asset);
		return asset;
	}

	public bool TryGetAsset(int fileIndex, long pathID, [NotNullWhen(true)] out IUnityObjectBase? asset)
	{
		ThrowIfFileIndexOutOfRange(fileIndex);
		AssetCollection? file = Dependencies[fileIndex];
		if (file is not null)
		{
			return file.TryGetAsset(pathID, out asset);
		}
		else
		{
			asset = null;
			return false;
		}
	}

	public bool TryGetAsset<T>(int fileIndex, long pathID, [NotNullWhen(true)] out T? asset) where T : IUnityObjectBase
	{
		ThrowIfFileIndexOutOfRange(fileIndex);
		AssetCollection? file = Dependencies[fileIndex];
		if (file is not null)
		{
			return file.TryGetAsset(pathID, out asset);
		}
		else
		{
			asset = default;
			return false;
		}
	}

	public IUnityObjectBase GetAsset(PPtr pptr) => GetAsset(pptr.FileID, pptr.PathID);

	public T GetAsset<T>(PPtr<T> pptr) where T : IUnityObjectBase
	{
		return GetAsset<T>(pptr.FileID, pptr.PathID);
	}

	public IUnityObjectBase? TryGetAsset(PPtr pptr) => TryGetAsset(pptr.FileID, pptr.PathID);

	public T? TryGetAsset<T>(PPtr<T> pptr) where T : IUnityObjectBase
	{
		return TryGetAsset<T>(pptr.FileID, pptr.PathID);
	}

	public bool TryGetAsset(PPtr pptr, [NotNullWhen(true)] out IUnityObjectBase? asset) => TryGetAsset(pptr.FileID, pptr.PathID, out asset);

	public bool TryGetAsset<T>(PPtr<T> pptr, [NotNullWhen(true)] out T? asset) where T : IUnityObjectBase
	{
		return TryGetAsset(pptr.FileID, pptr.PathID, out asset);
	}

	private void ThrowIfFileIndexOutOfRange(int fileIndex)
	{
		if (fileIndex < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(fileIndex), $"File index cannot be negative: {fileIndex}");
		}
		else if (fileIndex >= Dependencies.Count)
		{
			throw new ArgumentException($"{nameof(AssetCollection)} with index {fileIndex} was not found in dependencies", nameof(fileIndex));
		}
	}
	#endregion

	#region IReadOnlyCollection
	public IEnumerator<IUnityObjectBase> GetEnumerator() => assets.Values.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	public int Count => assets.Count;
	#endregion
}