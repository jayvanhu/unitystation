
using System.Collections;
using System.Linq;
using Construction;
using UnityEngine;

/// <summary>
/// Client requests to construct something using the material in their active hand.
/// </summary>
public class RequestBuildMessage : ClientMessage
{
	public static short MessageType = (short) MessageTypes.RequestBuildMessage;

	//index of the entry in the ConstructionList.
	public byte EntryIndex;

	public override IEnumerator Process()
	{
		var clientStorage = SentByPlayer.Script.ItemStorage;
		var usedSlot = clientStorage.GetActiveHandSlot();
		if (usedSlot == null || usedSlot.ItemObject == null) yield break;

		var hasConstructionMenu = usedSlot.ItemObject.GetComponent<BuildingMaterial>();
		if (hasConstructionMenu == null) yield break;

		var entry = hasConstructionMenu.BuildList.Entries.ToArray()[EntryIndex];

		if (!entry.CanBuildWith(hasConstructionMenu)) yield break;

		//check if the space to construct on is passable
		if (!MatrixManager.IsPassableAt((Vector3Int) SentByPlayer.GameObject.TileWorldPosition(), true, includingPlayers: false))
		{
			Chat.AddExamineMsg(SentByPlayer.GameObject, "It won't fit here.");
			yield break;
		}

		//if we are building something impassable, check if there is anything on the space other than the performer.
		var atPosition =
			MatrixManager.GetAt<RegisterTile>((Vector3Int) SentByPlayer.GameObject.TileWorldPosition(), true);
		var builtObjectIsImpassable = !entry.Prefab.GetComponent<RegisterTile>().IsPassable(true);
		foreach (var thingAtPosition in atPosition)
		{
			if (entry.OnePerTile)
			{
				//can only build one of this on a given tile
				if (entry.Prefab.Equals(Spawn.DeterminePrefab(thingAtPosition.gameObject)))
				{
					Chat.AddExamineMsg(SentByPlayer.GameObject, $"There's already one here.");
					yield break;
				}
			}

			if (builtObjectIsImpassable)
			{
				//if the object we are building is itself impassable, we should check if anything blocks construciton.
				//otherwise it's fine to add it to the pile on the tile
				if (ServerValidations.IsConstructionBlocked(SentByPlayer.GameObject, null,
					SentByPlayer.GameObject.TileWorldPosition())) yield break;
			}
		}

		//build and consume
		var finishProgressAction = new ProgressCompleteAction(() =>
		{
			if (entry.ServerBuild(SpawnDestination.At(SentByPlayer.Script.registerTile), hasConstructionMenu))
			{
				Chat.AddActionMsgToChat(SentByPlayer.GameObject, $"You finish building the {entry.Name}.",
					$"{SentByPlayer.GameObject.ExpensiveName()} finishes building the {entry.Name}.");
			}
		});

		Chat.AddActionMsgToChat(SentByPlayer.GameObject, $"You begin building the {entry.Name}...",
			$"{SentByPlayer.GameObject.ExpensiveName()} begins building the {entry.Name}...");
		ToolUtils.ServerUseTool(SentByPlayer.GameObject, usedSlot.ItemObject,
			SentByPlayer.Script.registerTile.WorldPositionServer.To2Int(), entry.BuildTime,
			finishProgressAction);
	}

	/// <summary>
	/// Request constructing the given entry
	/// </summary>
	/// <param name="entry">entry to build</param>
	/// <param name="hasMenu">has construction menu component of the object being used to
	/// construct.</param>
	/// <returns></returns>
	public static RequestBuildMessage Send(BuildList.Entry entry, BuildingMaterial hasMenu)
	{
		byte entryIndex = (byte) hasMenu.BuildList.Entries.ToList().IndexOf(entry);
		if (entryIndex == -1) return null;

		RequestBuildMessage msg = new RequestBuildMessage
		{
			EntryIndex = entryIndex
		};
		msg.Send();
		return msg;
	}
}
