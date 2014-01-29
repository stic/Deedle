﻿namespace Deedle

// --------------------------------------------------------------------------------------
// Indexing - index provides access to data in vector via keys. Optionally, the keys
// can be sorted. The index provides various operations for joining indices (etc.) that
// produce a new index, together with transformations to be applied on the data vector.
// Index is an interface and so you can define your own. 
// --------------------------------------------------------------------------------------

/// Represents different behaviors of key lookup in series. For unordered series,
/// the only available option is `Lookup.Exact` which finds the exact key - methods
/// fail or return missing value if the key is not available in the index. For ordered
/// series `Lookup.NearestGreater` finds the first greater key (e.g. later date) with
/// a value. `Lookup.NearestSmaller` searches for the first smaller key.
type Lookup = 
  /// Lookup a value associated with the exact specified key. 
  /// If the key is not available, then fail or return missing value. 
  | Exact = 0

  /// Lookup a value associated with the specified key or with the nearest
  /// greater key that has a value available. Fails (or returns missing value)
  /// only when the specified key is greater than all available keys.
  | NearestGreater = 1

  /// Lookup a value associated with the specified key or with the nearest
  /// smaller key that has a value available. Fails (or returns missing value)
  /// only when the specified key is smaller than all available keys.
  | NearestSmaller = 2

/// Represents a strategy for aggregating data in an ordered series into data segments.
/// To create a value of this type from C#, use the non-generic `Aggregation` type.
/// Data can be aggregate using floating windows or chunks of a specified size or 
/// by specifying a condition on two keys (i.e. end a window/chunk when the condition
/// no longer holds).
type Aggregation<'K> =
  /// Aggregate data into floating windows of a specified size 
  /// and the provided handling of boundary elements.
  | WindowSize of int * Boundary

  /// Aggregate data into non-overlapping chunks of a specified size 
  /// and the provided handling of boundary elements.
  | ChunkSize of int * Boundary

  /// Aggregate data into floating windows where each window ends as soon
  /// as the specified function returns `false` when called with the 
  /// first key and the current key as arguments.
  | WindowWhile of ('K -> 'K -> bool)

  /// Aggregate data into non-overlapping chunks where each chunk ends as soon
  /// as the specified function returns `false` when called with the 
  /// first key and the current key as arguments.
  | ChunkWhile of ('K -> 'K -> bool)


/// A non-generic type that simplifies the construction of `Aggregation<K>` values
/// from C#. It provides methods for constructing different kinds of aggregation
/// strategies for ordered series.
type Aggregation =
  /// Aggregate data into floating windows of a specified size 
  /// and the provided handling of boundary elements.
  ///
  /// ## Parameters
  ///
  ///  - `size` - Specifies the size of the floating window. Depending on the
  ///    boundary behavior, the actual created windows may be smaller.
  ///  - `boundary` - Specifies how to handle boundaries (when there is not
  ///    enough data to create an entire window). 
  static member WindowSize(size, boundary) = WindowSize(size, boundary)

  /// Aggregate data into non-overlapping chunks of a specified size 
  /// and the provided handling of boundary elements.
  ///
  /// ## Parameters
  ///
  ///  - `size` - Specifies the size of the floating window. Depending on the
  ///    boundary behavior, the actual created windows may be smaller.
  ///  - `boundary` - Specifies how to handle boundaries (when there is not
  ///    enough data to create an entire window). 
  static member ChunkSize(size, boundary) = ChunkSize(size, boundary)

  /// Aggregate data into floating windows where each window ends as soon
  /// as the specified function returns `false` when called with the 
  /// first key and the current key as arguments.
  ///
  /// ## Parameters
  ///
  ///  - `condition` - A delegate that specifies when to end the current window
  ///    (e.g. `(k1, k2) => k2 - k1 < 10` means that the difference between keys
  ///    in each window will be less than 10.
  static member WindowWhile<'K>(condition:System.Func<'K, 'K, bool>) = 
    WindowWhile(fun k1 k2 -> condition.Invoke(k1, k2))

  /// Aggregate data into non-overlapping chunks where each chunk ends as soon
  /// as the specified function returns `false` when called with the 
  /// first key and the current key as arguments.
  ///
  /// ## Parameters
  ///
  ///  - `condition` - A delegate that specifies when to end the current chunk
  ///    (e.g. `(k1, k2) => k2 - k1 < 10` means that the difference between keys
  ///    in each chunk will be less than 10.
  static member ChunkWhile<'K>(condition:System.Func<'K, 'K, bool>) = 
    ChunkWhile(fun k1 k2 -> condition.Invoke(k1, k2))

// --------------------------------------------------------------------------------------

namespace Deedle.Indices

open Deedle
open Deedle.Keys
open Deedle.Internal
open Deedle.Addressing
open Deedle.Vectors

/// Specifies the boundary behavior for the `IIndexBuilder.GetRange` operation
/// (whether the boundary elements should be included or not)
type BoundaryBehavior = Inclusive | Exclusive

/// An interface that represents index mapping keys of some generic type `T` to locations
/// of address `Address`. The `IIndex<K>` contains minimal set of operations that have to
/// be supported by an index. This type should be only used directly when
/// extending the DataFrame library and adding a new way of storing or loading data.
/// Values of this type are constructed using the associated `IIndexBuilder` type.
type IIndex<'K when 'K : equality> = 
  /// Returns a sequence of all keys in the index.
  abstract Keys : seq<'K>

  /// Performs reverse lookup - and returns key for a specified address
  abstract KeyAt : Address -> 'K

  /// Returns the number of keys in the index
  abstract KeyCount : int64

  /// Returns whether the specified index is empty. This is equivalent to 
  /// testing if `Keys` are empty, but it does not have to evaluate delayed index.
  abstract IsEmpty : bool

  /// Find the address associated with the specified key, or with the nearest
  /// key as specifeid by the `lookup` argument. The `condition` function is called
  /// when searching for keys to ask the caller whether the address should be returned
  /// (or whether to continue searching). This is used when searching for previous element
  /// in a series (where we need to check if a value at the address is available)
  abstract Lookup : key:'K * lookup:Lookup * condition:(Address -> bool) -> OptionalValue<'K * Address>  

  /// Returns all key-address mappings in the index
  abstract Mappings : seq<'K * Address>

  /// Returns the minimal and maximal key associated with the index.
  /// (the operation may fail for unordered indices)
  abstract KeyRange : 'K * 'K

  /// Returns `true` if the index is ordered and `false` otherwise
  abstract IsOrdered : bool

  /// Returns a comparer associated with the values used by the current index.
  abstract Comparer : System.Collections.Generic.Comparer<'K>

  /// Returns an index builder that canbe used for constructing new indices of the
  /// same kind as the current index (e.g. a lazy index returns a lazy index builder)
  abstract Builder : IIndexBuilder


/// Represents a pair of index and vector construction 
/// (many of the index operations take/return an index together with a construction
/// command that builds a vector matching with the index, so this type alias
/// makes this more obvious)
and SeriesConstruction<'K when 'K : equality> = IIndex<'K> * VectorConstruction

/// Asynchronous version of `SeriesConstruction<'K>`. Returns a workflow that evaluates
/// the index, together with a construction to apply (asynchronously) on vectors
and AsyncSeriesConstruction<'K when 'K : equality> = Async<IIndex<'K>> * VectorConstruction

/// A builder represents various ways of constructing index, either from keys or from
/// other indices. The operations that build a new index from an existing index also 
/// build `VectorConstruction` which specifies how to transform vectors aligned with the
/// previous index to match the new index. The methods generally take `VectorConstruction`
/// as an input, apply necessary transformations to it and return a new `VectorConstruction`.
///
/// ## Example
///
/// For example, given `index`, we can say:
///
///     // Create an index that excludes the value 42
///     let newIndex, vectorCmd = indexBuilder.DropItem(index, 42, VectorConstruction.Return(0))
///
///     // Now we can transform multiple vectors (e.g. all frame columns) using 'vectorCmd'
///     // (the integer '0' in `Return` is an offset in the array of vector arguments)
///     let newVector = vectorBuilder.Build(vectorCmd, [| vectorToTransform |])
///
and IIndexBuilder =
  
  /// Create a new index using the specified keys. Optionally, the caller can specify
  /// if the index keys are ordered or not. When the value is not set, the construction
  /// should check and infer this from the data.
  abstract Create : seq<'K> * Option<bool> -> IIndex<'K>
  
  /// When we perform some projection on the vector (e.g. `Series.map`), then we may also
  /// need to perform some transformation on the index (because it will typically turn delayed
  /// index into an evaluated index). This operation represents that - it should return 
  /// (evaluated) index with the same keys.
  abstract Project : IIndex<'K> -> IIndex<'K>

  /// Create a new index that represents sub-range of an existing index. The range is specified
  /// as a pair of options (when `None`, the original left/right boundary should be used) 
  /// that contain boundary behavior and the boundary key.
  abstract GetRange : 
    IIndex<'K> * option<'K * BoundaryBehavior> * option<'K * BoundaryBehavior> * VectorConstruction ->
    SeriesConstruction<'K>

  /// Creates a union of two indices and builds corresponding vector transformations
  /// for both vectors that match the left and the right index.
  abstract Union : 
    SeriesConstruction<'K> * SeriesConstruction<'K> ->
    IIndex<'K> * VectorConstruction * VectorConstruction

  /// Creates an interesection of two indices and builds corresponding vector transformations
  /// for both vectors that match the left and the right index.
  abstract Intersect :
    SeriesConstruction<'K> * SeriesConstruction<'K> ->
    IIndex<'K> * VectorConstruction * VectorConstruction

  /// Append two indices and builds corresponding vector transformations
  /// for both vectors that match the left and the right index. If the indices
  /// are ordered, the ordering should be preserved (the keys should be aligned).
  /// The specified `IVectorValueTransform` defines how to deal with the case when
  /// a key is defined in both indices (i.e. which value should be in the new vector).
  abstract Append :
    SeriesConstruction<'K> * SeriesConstruction<'K> * IVectorValueTransform -> 
    IIndex<'K> * VectorConstruction

  /// Given an old index and a new index, build a vector transformation that reorders
  /// elements from a vector associated with the old index so that they match the new
  /// index. When finding element location in the new index, the provided `Lookup` strategy
  /// is used. This is used, for example, when doing left/right join (to align the new data
  /// with another index) or when selecting multiple keys (`Series.lookupAll`).
  ///
  /// The proivded `condition` is used when searching for a value in the old index
  /// (when lookup is not exact). It is called to check that the address contains an
  /// appropriate value (e.g. when we need to skip over missing values).
  abstract Reindex :
    IIndex<'K> * IIndex<'K> * Lookup * VectorConstruction * (Address -> bool) -> VectorConstruction

  /// Create a new index by picking a new key value for each key in the original index
  /// (used e.g. when we have a frame and want to use specified column as a new index).
  abstract WithIndex :
    IIndex<'K> * (Address -> OptionalValue<'TNewKey>) * VectorConstruction -> 
    SeriesConstruction<'TNewKey>

  /// Drop an item associated with the specified key from the index. 
  abstract DropItem : SeriesConstruction<'K> * 'K -> SeriesConstruction<'K> 

  /// Get items associated with the specified key from the index. This method takes
  /// `ICustomLookup<K>` which provides an implementation of `ICustomKey<K>`. This 
  /// is used for custom equality testing (for example, when getting a level of a hierarchical index)
  abstract LookupLevel : SeriesConstruction<'K> * ICustomLookup<'K> -> SeriesConstruction<'K>

  /// Order (possibly unordered) index and return transformation that reorders vector
  abstract OrderIndex : SeriesConstruction<'K> -> SeriesConstruction<'K>

  /// Aggregate an ordered index into floating windows or chunks. 
  ///
  /// ## Parameters
  ///
  ///  - `index` - Specifies the index to be aggregated
  ///  - `aggregation` - Defines the kind of aggregation to apply (the type 
  ///    is a discriminated union with a couple of cases)
  ///  - `source` - Source vector construction to be transformed 
  ///  - `selector` - Given information about window/chunk (including 
  ///    vector construction that can be used to build the data chunk), return
  ///    a new key, together with a new value for the returned vector.
  abstract Aggregate : index:IIndex<'K> * aggregation:Aggregation<'K> * source:VectorConstruction *
    selector:(DataSegmentKind * SeriesConstruction<'K> -> 'TNewKey * OptionalValue<'R>)
      -> IIndex<'TNewKey> * IVector<'R>

  /// Group a (possibly unordered) index according to a provided sequence of labels.
  /// The operation results in a sequence of unique labels along with corresponding 
  /// series construction objects which can be applied to arbitrary vectors/columns.
  abstract GroupWith : index:IIndex<'K> * labels:seq<'TNewKey> * VectorConstruction -> 
    ('TNewKey * SeriesConstruction<'K>) seq when 'TNewKey : equality

  /// Group a (possibly unordered) index using the specified `keySelector` function.
  /// The operation builds a new index with the selected keys and a matching vector
  /// with values produced by the `valueSelector` function.
  abstract GroupBy : IIndex<'K> * keySelector:('K -> OptionalValue<'TNewKey>) * VectorConstruction *
    valueSelector:('TNewKey * SeriesConstruction<'K> -> OptionalValue<'R>) -> IIndex<'TNewKey> * IVector<'R>


  /// Aggregate data into non-overlapping chunks by aligning them to the
  /// specified keys. The second parameter specifies the direction. If it is
  /// `Direction.Forward` than the key is the first element of a chunk; for 
  /// `Direction.Backward`, the key is the last element (note that this does not 
  /// hold at the boundaries where values before/after the key may also be included)
  abstract Resample : IIndex<'K> * seq<'K> * Direction * source:VectorConstruction *
    valueSelector:('TNewKey * SeriesConstruction<'K> -> OptionalValue<'R>) *
    keySelector:('K * SeriesConstruction<'K> -> 'TNewKey) -> IIndex<'TNewKey> * IVector<'R>

  /// Given an index and vector construction, return a new index asynchronously
  /// to allow composing evaluation of lazy series. The command to be applied to
  /// vectors can be applied asynchronously using `vectorBuilder.AsyncBuild`
  abstract AsyncMaterialize : SeriesConstruction<'K> -> AsyncSeriesConstruction<'K>