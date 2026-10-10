using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RaidAnimalFix;

/// <summary>Кнопка «Дипломатия» — только в мультиплеере (в одиночной игре фракций игроков нет).</summary>
public class MainButtonWorker_RafDiplomacy : MainButtonWorker_ToggleTab
{
	public override bool Visible
	{
		get
		{
			if (!base.Visible)
			{
				return false;
			}
			return RelationsUtil.InMultiplayer;
		}
	}
}

/// <summary>Подписи для UI (только чтение).</summary>
public static class RelationsUi
{
	public static string KindLabel(RafRelationKind k)
	{
		switch (k)
		{
		case RafRelationKind.Ally:
			return "RAF_Relations_KindAlly".Translate();
		case RafRelationKind.War:
			return "RAF_Relations_KindWar".Translate();
		default:
			return "RAF_Relations_KindNeutral".Translate();
		}
	}

	public static Color KindColor(RafRelationKind k)
	{
		switch (k)
		{
		case RafRelationKind.Ally:
			return new Color(0.5f, 0.9f, 0.5f);
		case RafRelationKind.War:
			return new Color(0.95f, 0.4f, 0.4f);
		default:
			return Color.white;
		}
	}

	public static string ActionLabel(int action)
	{
		switch (action)
		{
		case RafDiploAction.ProposeAlliance:
			return "RAF_Relations_BtnProposeAlliance".Translate();
		case RafDiploAction.ProposePeace:
			return "RAF_Relations_BtnProposePeace".Translate();
		case RafDiploAction.DeclareWar:
			return "RAF_Relations_BtnDeclareWar".Translate();
		case RafDiploAction.BreakAlliance:
			return "RAF_Relations_BtnBreakAlliance".Translate();
		case RafDiploAction.Accept:
			return "RAF_Relations_BtnAccept".Translate();
		case RafDiploAction.Decline:
			return "RAF_Relations_BtnDecline".Translate();
		case RafDiploAction.Withdraw:
			return "RAF_Relations_BtnWithdraw".Translate();
		case RafDiploAction.CancelPending:
			return "RAF_Relations_BtnCancelPending".Translate();
		default:
			return "?";
		}
	}

	public static string PermLabel(RafGuestPerm p)
	{
		switch (p)
		{
		case RafGuestPerm.Designate:
			return "RAF_Relations_PermDesignate".Translate();
		case RafGuestPerm.Food:
			return "RAF_Relations_PermFood".Translate();
		case RafGuestPerm.Beds:
			return "RAF_Relations_PermBeds".Translate();
		default:
			return "RAF_Relations_PermForbid".Translate();
		}
	}
}

/// <summary>
/// Окно дипломатии. Только UI: читает синхронное состояние RafRelationsData и
/// вызывает [SyncMethod]-ы с фракцией локального игрока (MP.RealPlayerFaction);
/// подлинность «from» дополнительно проверяется в самой команде.
/// </summary>
public class MainTabWindow_RafDiplomacy : MainTabWindow
{
	private static readonly int[] Actions = new int[8]
	{
		RafDiploAction.Accept,
		RafDiploAction.Decline,
		RafDiploAction.ProposeAlliance,
		RafDiploAction.ProposePeace,
		RafDiploAction.Withdraw,
		RafDiploAction.CancelPending,
		RafDiploAction.BreakAlliance,
		RafDiploAction.DeclareWar
	};

	private Vector2 scroll;

	private float lastHeight = 600f;

	public override Vector2 RequestedTabSize => new Vector2(1000f, 620f);

	public override void DoWindowContents(Rect inRect)
	{
		RafRelationsData data = RafRelationsData.Get();
		Faction me = RelationsUtil.LocalFaction();
		Text.Font = GameFont.Medium;
		Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 35f), "RAF_Relations_Title".Translate());
		Text.Font = GameFont.Small;
		Rect body = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);
		if (data == null || me == null || !RelationsUtil.InMultiplayer || !MpContext.IsRealPlayerFaction(me))
		{
			Widgets.Label(body, "RAF_Relations_OnlyPlayers".Translate());
			return;
		}
		List<Faction> others = RelationsUtil.RealPlayerFactions();
		others.Remove(me);
		int now = RelationsUtil.NowWorldTicks();
		Rect view = new Rect(0f, 0f, body.width - 16f, lastHeight);
		Widgets.BeginScrollView(body, ref scroll, view);
		float y = 0f;
		try
		{
			if (others.Count == 0)
			{
				Widgets.Label(new Rect(0f, y, view.width, 30f), "RAF_Relations_NoOthers".Translate());
				y += 30f;
			}
			else
			{
				y = DrawRelations(view.width, y, data, me, others, now);
				y += 16f;
				y = DrawPermissions(view.width, y, data, me, others);
			}
		}
		finally
		{
			lastHeight = Mathf.Max(y + 10f, body.height - 4f);
			Widgets.EndScrollView();
		}
	}

	private float DrawRelations(float width, float y, RafRelationsData data, Faction me, List<Faction> others, int now)
	{
		Text.Font = GameFont.Small;
		GUI.color = Color.gray;
		Widgets.Label(new Rect(0f, y, 220f, 24f), "RAF_Relations_ColFaction".Translate());
		Widgets.Label(new Rect(225f, y, 230f, 24f), "RAF_Relations_ColStatus".Translate());
		Widgets.Label(new Rect(460f, y, width - 460f, 24f), "RAF_Relations_ColActions".Translate());
		GUI.color = Color.white;
		y += 26f;
		for (int i = 0; i < others.Count; i++)
		{
			Faction f = others[i];
			float rowH = 58f;
			Rect row = new Rect(0f, y, width, rowH);
			if (i % 2 == 0)
			{
				Widgets.DrawLightHighlight(row);
			}
			RafRelationKind kind = data.KindOf(me, f);
			RafRelationPair p = data.FindPair(me.loadID, f.loadID);
			Widgets.Label(new Rect(4f, y + 4f, 216f, 50f), f.Name);
			GUI.color = RelationsUi.KindColor(kind);
			Widgets.Label(new Rect(225f, y + 4f, 230f, 24f), RelationsUi.KindLabel(kind));
			GUI.color = Color.gray;
			Text.Font = GameFont.Tiny;
			Widgets.Label(new Rect(225f, y + 26f, 230f, 32f), StatusDetails(data, p, me, f, now));
			Text.Font = GameFont.Small;
			GUI.color = Color.white;
			float x = 460f;
			bool any = false;
			for (int a = 0; a < Actions.Length; a++)
			{
				int action = Actions[a];
				if (!data.CanDo(me, f, action, now, out var _))
				{
					continue;
				}
				any = true;
				string label = RelationsUi.ActionLabel(action);
				float w = Mathf.Max(90f, Text.CalcSize(label).x + 16f);
				if (x + w > width)
				{
					break;
				}
				if (Widgets.ButtonText(new Rect(x, y + 14f, w, 30f), label))
				{
					Click(me, f, action);
				}
				x += w + 6f;
			}
			if (!any)
			{
				data.CanDo(me, f, RafDiploAction.DeclareWar, now, out var reason);
				GUI.color = Color.gray;
				Widgets.Label(new Rect(460f, y + 18f, width - 460f, 30f), reason ?? "");
				GUI.color = Color.white;
			}
			y += rowH + 2f;
		}
		return y;
	}

	private static string StatusDetails(RafRelationsData data, RafRelationPair p, Faction me, Faction f, int now)
	{
		List<string> parts = new List<string>();
		if (p != null && p.pendingKind >= 0)
		{
			string who = (p.pendingBy == me.loadID) ? me.Name : f.Name;
			string left = Math.Max(0, p.pendingAt - now).ToStringTicksToPeriod();
			parts.Add(((p.pendingKind == 2) ? "RAF_Relations_StatusWarPending" : "RAF_Relations_StatusBreakPending").Translate(who, left));
		}
		RafProposal theirs = data.FindProposal(f.loadID, me.loadID);
		if (theirs != null)
		{
			parts.Add(((theirs.kind == 1) ? "RAF_Relations_StatusTheyProposeAlliance" : "RAF_Relations_StatusTheyProposePeace").Translate());
		}
		RafProposal mine = data.FindProposal(me.loadID, f.loadID);
		if (mine != null)
		{
			parts.Add(((mine.kind == 1) ? "RAF_Relations_StatusYouProposedAlliance" : "RAF_Relations_StatusYouProposedPeace").Translate());
		}
		if (p != null)
		{
			int cd = p.lastChangeTick + RafRelationsData.CooldownTicks - now;
			if (cd > 0)
			{
				parts.Add("RAF_Relations_StatusCooldown".Translate(cd.ToStringTicksToPeriod()));
			}
		}
		return string.Join("; ", parts.ToArray());
	}

	private static void Click(Faction me, Faction f, int action)
	{
		if (action == RafDiploAction.DeclareWar || action == RafDiploAction.BreakAlliance)
		{
			string key = (action == RafDiploAction.DeclareWar) ? "RAF_Relations_ConfirmWar" : "RAF_Relations_ConfirmBreak";
			Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(key.Translate(f.Name, RafRelationsData.PendingDelayTicks.ToStringTicksToPeriod()), delegate
			{
				RafRelationsData.SyncDiploAction(me, f, action);
			}, destructive: true));
			return;
		}
		RafRelationsData.SyncDiploAction(me, f, action);
	}

	private float DrawPermissions(float width, float y, RafRelationsData data, Faction me, List<Faction> others)
	{
		Widgets.DrawLineHorizontal(0f, y, width);
		y += 6f;
		Text.Font = GameFont.Medium;
		Widgets.Label(new Rect(0f, y, width, 32f), "RAF_Relations_PermTitle".Translate());
		Text.Font = GameFont.Small;
		y += 32f;
		GUI.color = Color.gray;
		Text.Font = GameFont.Tiny;
		Widgets.Label(new Rect(0f, y, width, 34f), "RAF_Relations_PermHint".Translate());
		Text.Font = GameFont.Small;
		GUI.color = Color.white;
		y += 36f;
		float colW = 150f;
		float x0 = 230f;
		GUI.color = Color.gray;
		for (int i = 0; i < 4; i++)
		{
			Widgets.Label(new Rect(x0 + i * colW, y, colW, 24f), RelationsUi.PermLabel((RafGuestPerm)i));
		}
		GUI.color = Color.white;
		y += 26f;
		// «Все союзники» — значения по умолчанию для союзников без своей записи.
		y = DrawPermRow(width, y, data, me, null, x0, colW, 0);
		for (int j = 0; j < others.Count; j++)
		{
			y = DrawPermRow(width, y, data, me, others[j], x0, colW, j + 1);
		}
		return y;
	}

	private float DrawPermRow(float width, float y, RafRelationsData data, Faction me, Faction guest, float x0, float colW, int index)
	{
		float rowH = 30f;
		Rect row = new Rect(0f, y, width, rowH);
		if (index % 2 == 0)
		{
			Widgets.DrawLightHighlight(row);
		}
		int guestId = guest?.loadID ?? (-1);
		RafGuestPerms explicitEntry = data.FindPerms(me.loadID, guestId);
		bool war = guest != null && data.KindOf(me, guest) == RafRelationKind.War;
		string name = (guest == null) ? ((string)"RAF_Relations_PermAllAllies".Translate()) : (guest.Name + " (" + RelationsUi.KindLabel(data.KindOf(me, guest)) + ")");
		Widgets.Label(new Rect(4f, y + 4f, x0 - 8f, 24f), name.Truncate(x0 - 8f));
		for (int i = 0; i < 4; i++)
		{
			RafGuestPerm perm = (RafGuestPerm)i;
			bool cur;
			if (guest == null)
			{
				cur = explicitEntry == null || explicitEntry.Get(perm);
			}
			else
			{
				cur = !war && data.EffectivePermIgnoringWar(me, guest, perm);
			}
			bool val = cur;
			Widgets.Checkbox(new Vector2(x0 + i * colW + 4f, y + 3f), ref val, 24f, war);
			if (val != cur && !war)
			{
				RafRelationsData.SyncSetGuestPerm(me, guestId, i, val);
			}
		}
		if (explicitEntry != null)
		{
			Rect btn = new Rect(width - 110f, y + 2f, 104f, 26f);
			if (Widgets.ButtonText(btn, "RAF_Relations_PermReset".Translate()))
			{
				RafRelationsData.SyncSetGuestPerm(me, guestId, -1, value: false);
			}
			TooltipHandler.TipRegion(btn, "RAF_Relations_PermResetTip".Translate());
		}
		if (war)
		{
			TooltipHandler.TipRegion(row, "RAF_Relations_PermWarTip".Translate());
		}
		return y + rowH + 2f;
	}
}
