# React to collisions and triggers on tiles

Find out which tile a body touches, so you can react to it. For example, you can break a tile, play a sound for the surface, or damage a character standing in lava.

Every shape a Physics Area Tilemap component creates remembers the cell of the tile it came from. When a contact or trigger event names one of these shapes, pass the shape to `PhysicsAreaTilemap.TryGetCell` to get the cell. Then use the cell with Tilemap component methods, such as [`Tilemap.GetTile`](https://docs.unity3d.com/ScriptReference/Tilemaps.Tilemap.GetTile.html).

## Prerequisites

Add a Physics Area Tilemap component to your tilemap. For more information, refer to [Add a Physics Area Tilemap component](PhysicsAreaTilemap.md).

## Set up events

To receive events from the tiles, set the **Definition** and **Callbacks** sections of the Physics Area Tilemap component, the same way as for the other physics area components. For more information, refer to the [Physics Area Circle component reference](https://docs.unity.com/en-us/engine/latest/manual/unity2d/2d-physics-api/create-objects/reference-area-circle).

Set **Callback Source** depending on which tiles you need to know about:

- **Shapes**: You get a begin and an end event for every tile a body touches. Use it when you need every tile, such as a character walking across tiles that crumble.
- **Events** or **Area**: The component combines the events between the tilemap and each other physics area, so you get one begin event when the area first touches any of the tiles, and one end event when it stops touching all of them. You get the first and the last tile only. Use it when you need to know only that a body touches the tilemap, such as a character landing on the ground. This requires the world's `eventGroupingAllowed` setting, which is enabled by default.

## Find the tile from an event

To find the tile from an event, pass the event's shapes to `PhysicsAreaTilemap.TryGetCell`. `TryGetCell` returns `false` if the shape isn't one of the area's shapes, or if the shape no longer exists. A contact event names two shapes, and the tile's shape can be either of them, so try both.

The following script logs the tile a body touches:

```csharp
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TileContacts : MonoBehaviour, PhysicsCallbacks.IContactCallback
{
    PhysicsAreaTilemap m_Area;
    Tilemap m_Tilemap;

    void Awake()
    {
        m_Area = GetComponent<PhysicsAreaTilemap>();
        m_Tilemap = GetComponent<Tilemap>();
    }

    public void OnContactBegin2D(PhysicsEvents.ContactBeginEvent beginEvent)
    {
        if (m_Area.TryGetCell(beginEvent.shapeA, out var cell) || m_Area.TryGetCell(beginEvent.shapeB, out cell))
            Debug.Log($"Touched {m_Tilemap.GetTile(cell)} at {cell}");
    }

    public void OnContactEnd2D(PhysicsEvents.ContactEndEvent endEvent) { }
}
```

To use the script, follow these steps:

1. Add the script to the GameObject of the tilemap.
2. Set the script as the **Callback Target**.
3. Set **Callback Source** to **Shapes**.

With **Callback Source** set to **Area**, implement `PhysicsAreaCallbacks.IContactCallback` instead. Its events have `areaA` and `areaB`, which are the two areas, and `beginEvent` or `endEvent`, which is the original shape event. Pass the shapes of the original event to `TryGetCell`:

```csharp
public class TileAreaContacts : MonoBehaviour, PhysicsAreaCallbacks.IContactCallback
{
    PhysicsAreaTilemap m_Area;
    Tilemap m_Tilemap;

    void Awake()
    {
        m_Area = GetComponent<PhysicsAreaTilemap>();
        m_Tilemap = GetComponent<Tilemap>();
    }

    public void OnContactBegin2D(PhysicsAreaCallbacks.ContactBeginEvent areaEvent)
    {
        var beginEvent = areaEvent.beginEvent;
        if (m_Area.TryGetCell(beginEvent.shapeA, out var cell) || m_Area.TryGetCell(beginEvent.shapeB, out cell))
            Debug.Log($"Touched {m_Tilemap.GetTile(cell)} at {cell}");
    }

    public void OnContactEnd2D(PhysicsAreaCallbacks.ContactEndEvent areaEvent) { }
}
```

> [!NOTE]
> An end event can arrive after the tile's shape is gone, for example when a tile is erased while a body touches it, or when the component rebuilds the shapes. `TryGetCell` then returns `false`. If you need the cell in end events, store it when the contact begins, keyed by the tile's shape.

## Find the tile from a contact hit event

A contact hit event happens when a body hits a tile faster than the world's `contactHitEventThreshold`, which is a speed in meters per second. Use it to react to impacts, for example to play a sound or break a tile when something lands on it fast enough. The event's `approachSpeed` is how fast the body was moving toward the tile.

The component doesn't send contact hit events to the **Callback Target**. Read them from the world after each simulation step, in a script on the GameObject of the tilemap:

```csharp
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TileImpacts : MonoBehaviour
{
    PhysicsAreaTilemap m_Area;
    Tilemap m_Tilemap;

    void Awake()
    {
        m_Area = GetComponent<PhysicsAreaTilemap>();
        m_Tilemap = GetComponent<Tilemap>();
    }

    void OnEnable() => PhysicsEvents.PostSimulate += OnPostSimulate;
    void OnDisable() => PhysicsEvents.PostSimulate -= OnPostSimulate;

    void OnPostSimulate(PhysicsWorld world, float deltaTime)
    {
        foreach (var hit in world.contactHitEvents)
        {
            if (m_Area.TryGetCell(hit.shapeA, out var cell) || m_Area.TryGetCell(hit.shapeB, out cell))
                Debug.Log($"Hit {m_Tilemap.GetTile(cell)} at {hit.approachSpeed} m/s");
        }
    }
}
```

## Use the tile in pre-contact, pre-continuous, and contact filter callbacks

Pre-contact, pre-continuous, and contact filter callbacks work differently from the other callbacks. The physics engine calls them in the middle of a simulation step, and can call them from any thread, not only the main thread. Most Unity APIs work only on the main thread and throw an exception on other threads. In these callbacks, you can't call `Tilemap` methods such as `GetTile`, or `GetComponent`.

`TryGetCell` is safe to call from any thread, so you can still find out which cell a body touches. To act on the tile in that cell, do the following:

1. On the main thread, before the simulation runs, look up the tile data you need and store it by cell.
2. In the callback, use `TryGetCell` to get the cell, then read the data you stored for it.

The following example lets bodies pass through one kind of tile. It records the cells of those tiles when the script starts, and cancels any contact with them in the pre-contact callback.

```csharp
using System.Collections.Generic;
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.Tilemaps;

public class PassThroughTiles : MonoBehaviour, PhysicsCallbacks.IPreContactCallback
{
    public TileBase passThroughTile;

    readonly HashSet<Vector3Int> m_PassThroughCells = new();
    PhysicsAreaTilemap m_Area;

    void Start()
    {
        // Start runs on the main thread, so GetComponent and Tilemap methods are safe here.
        m_Area = GetComponent<PhysicsAreaTilemap>();
        var tilemap = GetComponent<Tilemap>();
        foreach (var cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.GetTile(cell) == passThroughTile)
                m_PassThroughCells.Add(cell);
        }
    }

    // Can run on any thread, so it only calls TryGetCell and reads the cells stored in Start.
    public void OnPreContact2D(ref PhysicsEvents.PreContactEvent preContactEvent)
    {
        if (IsPassThrough(preContactEvent.shapeA) || IsPassThrough(preContactEvent.shapeB))
            preContactEvent.Cancel();
    }

    bool IsPassThrough(PhysicsShape shape) => m_Area.TryGetCell(shape, out var cell) && m_PassThroughCells.Contains(cell);
}
```

To use the script, follow these steps:

1. Add the script to the GameObject of the tilemap.
2. Set the script as the **Callback Target**.
3. Set **Callback Source** to **Shapes**.
4. Enable **Pre Contact Callbacks** on the tilemap and in the world.

If you paint or erase pass-through tiles during gameplay, update the stored cells on the main thread at the same time. Never change the stored data from inside the callback.

## Additional resources

- [2D Physics Core package](https://docs.unity3d.com/Packages/com.unity.2d.physics@latest)
- [Add a Physics Area Tilemap component](PhysicsAreaTilemap.md)
- [Tilemap component reference](https://docs.unity3d.com/Manual/tilemaps/tilemap-reference.html)
