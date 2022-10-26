﻿using Maple2Storage.Enums;
using Maple2Storage.Types.Metadata;
using MaplePacketLib2.Tools;
using MapleServer2.Constants;
using MapleServer2.Data.Static;
using MapleServer2.Enums;
using MapleServer2.Managers;
using MapleServer2.Managers.Actors;
using MapleServer2.Packets;
using MapleServer2.Servers.Game;
using MapleServer2.Types;

namespace MapleServer2.PacketHandlers.Game;

public class RideHandler : GamePacketHandler<RideHandler>
{
    public override RecvOp OpCode => RecvOp.RequestRide;

    private enum Mode : byte
    {
        StartRide = 0x0,
        StopRide = 0x1,
        ChangeRide = 0x2,
        StartMultiPersonRide = 0x3,
        StopMultiPersonRide = 0x4
    }

    public override void Handle(GameSession session, PacketReader packet)
    {
        Mode mode = (Mode) packet.ReadByte();

        switch (mode)
        {
            case Mode.StartRide:
                HandleStartRide(session, packet);
                break;
            case Mode.StopRide:
                HandleStopRide(session, packet);
                break;
            case Mode.ChangeRide:
                HandleChangeRide(session, packet);
                break;
            case Mode.StartMultiPersonRide:
                HandleStartMultiPersonRide(session, packet);
                break;
            case Mode.StopMultiPersonRide:
                HandleStopMultiPersonRide(session);
                break;
            default:
                LogUnknownMode(mode);
                break;
        }
    }

    private static void HandleStartRide(GameSession session, PacketReader packet)
    {
        RideType type = (RideType) packet.ReadByte();
        int mountId = packet.ReadInt();
        packet.ReadLong();
        long mountUid = packet.ReadLong();
        // [46-0s] (UgcPacketHelper.Ugc()) but client doesn't set this data?

        MapUi mapUi = MapMetadataStorage.GetMapUi(session.Player.MapId);
        if (!mapUi.EnableMount)
        {
            session.Send(NoticePacket.Notice(SystemNotice.ErrCannotUseHere, NoticeType.Chat | NoticeType.FastText));
            return;
        }

        if (type == RideType.UseItem && !session.Player.Inventory.HasItem(mountUid))
        {
            return;
        }

        Item item = session.Player.Inventory.GetByUid(mountUid);
        if (item.IsExpired())
        {
            return;
        }

        IFieldObject<Mount> fieldMount = session.FieldManager.RequestFieldObject(new Mount
        {
            Type = type,
            Id = mountId,
            Uid = mountUid,
            Ugc = item.Ugc
        });

        fieldMount.Value.Players[0] = session.Player.FieldPlayer;
        session.Player.Mount = fieldMount;

        if (item.TransferFlag.HasFlag(ItemTransferFlag.Binds) && !item.IsBound())
        {
            item.BindItem(session.Player);
        }

        session.FieldManager.BroadcastPacket(MountPacket.StartRide(session.Player.FieldPlayer));
    }

    private static void HandleStopRide(GameSession session, PacketReader packet)
    {
        packet.ReadByte();
        bool forced = packet.ReadBool(); // Going into water without amphibious riding

        session.Player.Mount = null; // Remove mount from player
        session.FieldManager.BroadcastPacket(MountPacket.StopRide(session.Player.FieldPlayer, forced));
    }

    private static void HandleChangeRide(GameSession session, PacketReader packet)
    {
        int mountId = packet.ReadInt();
        long mountUid = packet.ReadLong();

        if (!session.Player.Inventory.HasItem(mountUid))
        {
            return;
        }

        Item item = session.Player.Inventory.GetByUid(mountUid);
        if (item.IsExpired())
        {
            return;
        }

        if (item.TransferFlag.HasFlag(ItemTransferFlag.Binds) && !item.IsBound())
        {
            item.BindItem(session.Player);
        }

        PacketWriter changePacket = MountPacket.ChangeRide(session.Player.FieldPlayer.ObjectId, mountId, mountUid);
        session.FieldManager.BroadcastPacket(changePacket);
    }

    private static void HandleStartMultiPersonRide(GameSession session, PacketReader packet)
    {
        int otherPlayerObjectId = packet.ReadInt();

        if (!session.FieldManager.State.Players.TryGetValue(otherPlayerObjectId, out Character otherPlayer) || otherPlayer.Value.Mount == null)
        {
            return;
        }

        bool isFriend = BuddyManager.IsFriend(session.Player, otherPlayer.Value);
        bool isGuildMember = session.Player != null && otherPlayer.Value.Guild != null && session.Player.Guild.Id == otherPlayer.Value.Guild.Id;
        bool isPartyMember = session.Player.Party == otherPlayer.Value.Party;

        if (!isFriend && !isGuildMember && !isPartyMember)
        {
            return;
        }

        int index = Array.FindIndex(otherPlayer.Value.Mount.Value.Players, 0, otherPlayer.Value.Mount.Value.Players.Length, x => x == null);
        otherPlayer.Value.Mount.Value.Players[index] = session.Player.FieldPlayer;
        session.Player.Mount = otherPlayer.Value.Mount;
        session.FieldManager.BroadcastPacket(MountPacket.StartTwoPersonRide(otherPlayerObjectId, session.Player.FieldPlayer.ObjectId, (byte) (index - 1)));
    }

    private static void HandleStopMultiPersonRide(GameSession session)
    {
        IFieldObject<Player> otherPlayer = session.Player.Mount.Value.Players[0];
        if (otherPlayer == null)
        {
            return;
        }

        session.FieldManager.BroadcastPacket(MountPacket.StopTwoPersonRide(otherPlayer.ObjectId, session.Player.FieldPlayer.ObjectId));
        session.Player.Move(otherPlayer.Coord, otherPlayer.Rotation);
        session.Player.Mount = null;
        if (otherPlayer.Value.Mount != null)
        {
            int index = Array.FindIndex(otherPlayer.Value.Mount.Value.Players, 0, otherPlayer.Value.Mount.Value.Players.Length,
                x => x.ObjectId == session.Player.FieldPlayer.ObjectId);
            otherPlayer.Value.Mount.Value.Players[index] = null;
        }
    }
}
