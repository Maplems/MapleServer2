﻿using Maple2Storage.Enums;
using MaplePacketLib2.Tools;
using MapleServer2.Constants;
using MapleServer2.Data.Static;
using MapleServer2.Managers.Actors;
using MapleServer2.Packets;
using MapleServer2.Servers.Game;
using MapleServer2.Types;

namespace MapleServer2.PacketHandlers.Game;

public class InsigniaHandler : GamePacketHandler<InsigniaHandler>
{
    public override RecvOp OpCode => RecvOp.Insignia;

    public override void Handle(GameSession session, PacketReader packet)
    {
        short insigniaId = packet.ReadShort();

        if (insigniaId < 0 && !InsigniaMetadataStorage.IsValid(insigniaId))
        {
            return;
        }

        Character? character = session.Player.FieldPlayer;
        if (character is null)
        {
            return;
        }

        session.Player.InsigniaId = insigniaId;
        session.FieldManager.BroadcastPacket(InsigniaPacket.UpdateInsignia(character.ObjectId, insigniaId,
            CanEquipInsignia(session, insigniaId)));
    }

    private static bool CanEquipInsignia(GameSession session, short insigniaId)
    {
        string? type = InsigniaMetadataStorage.GetConditionType(insigniaId);

        switch (type) // TODO: handling survivallevel
        {
            case "vip":
                return session.Player.Account.IsVip();
            case "level":
                return session.Player.Levels.Level >= 50;
            case "enchant":
                KeyValuePair<ItemSlot, Item>? firstOrDefault = session.Player.Inventory.Equips.FirstOrDefault(x => x.Value.EnchantLevel >= 12);
                return firstOrDefault?.Value is not null;
            case "trophy_point":
                return session.Player.TrophyCount[0] + session.Player.TrophyCount[1] + session.Player.TrophyCount[2] > 1000;
            case "title":
                int? titleId = InsigniaMetadataStorage.GetTitleId(insigniaId);
                return titleId is not null && session.Player.Titles.Contains((int) titleId);
            case "adventure_level":
                return session.Player.Account.Prestige.Level >= 100;
            default:
                Logger.Warning("Unhandled condition type for insigniaid: {insigniaId}, type: {type}", insigniaId, type);
                return false;
        }
    }
}
