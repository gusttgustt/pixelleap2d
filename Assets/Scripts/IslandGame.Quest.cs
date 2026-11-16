using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// Island people with jobs for you: the shipwright on every island (boat upgrades), and the
/// Glowing Mushroom island's doctor and her lost child. Quests ask you to bring materials, beat
/// creatures and help people; finishing one upgrades your boat or teaches you the island's tech.
public partial class IslandGame
{
    public class Quest
    {
        public string id, title;
        public (Item item, int n)[] bring = new (Item, int)[0];
        public (EnemyKind kind, int n)[] kill = new (EnemyKind, int)[0];
        public (string flag, string text)[] help = new (string, string)[0];
        public string rewardText;
        public System.Action reward;
    }

    /// Story state: "done:<quest>", "acc:<quest>", "child<island>", "king<island>"...
    readonly HashSet<string> flags = new HashSet<string>();
    /// Kills counted for an accepted quest: "<quest>|<enemy kind>" -> count.
    readonly Dictionary<string, int> questKills = new Dictionary<string, int>();

    public bool Flag(string f) => flags.Contains(f);
    bool Accepted(Quest q) => flags.Contains("acc:" + q.id);
    bool Done(Quest q) => flags.Contains("done:" + q.id);
    int Kills(Quest q, EnemyKind k) => questKills.TryGetValue(q.id + "|" + (int)k, out var n) ? n : 0;

    bool QuestReady(Quest q)
    {
        foreach (var b in q.bring) if (Count(b.item) < b.n) return false;
        foreach (var k in q.kill) if (Kills(q, k.kind) < k.n) return false;
        foreach (var h in q.help) if (!Flag(h.flag)) return false;
        return true;
    }

    /// One line per objective with a tick when it's done.
    List<string> QuestLines(Quest q)
    {
        var lines = new List<string>();
        foreach (var b in q.bring)
            lines.Add($"{(Count(b.item) >= b.n ? "✓" : "•")} นำ{Name(b.item)}มา {Mathf.Min(Count(b.item), b.n)}/{b.n}");
        foreach (var k in q.kill)
            lines.Add($"{(Kills(q, k.kind) >= k.n ? "✓" : "•")} ปราบ{EnemyName(k.kind)} {Mathf.Min(Kills(q, k.kind), k.n)}/{k.n}");
        foreach (var h in q.help)
            lines.Add($"{(Flag(h.flag) ? "✓" : "•")} {h.text}");
        return lines;
    }

    void TurnIn(Quest q)
    {
        if (!QuestReady(q)) return;
        foreach (var b in q.bring) inv[b.item] -= b.n;
        flags.Add("done:" + q.id);
        flags.Remove("acc:" + q.id);
        Sfx.Play("levelup", 0.6f);
        Fx.Burst(PlayerPos + Vector3.up * 0.5f, new Color(1f, 0.85f, 0.3f), 18, 1.4f, 2f, 1f, 3f);
        q.reward?.Invoke();
        SaveGame();
    }

    /// A creature died by your hand: count it for every accepted quest that wants it.
    void QuestKill(EnemyKind kind)
    {
        if (kind == EnemyKind.ShroomKing) flags.Add("king" + island);
        foreach (var f in flags)
        {
            if (!f.StartsWith("acc:")) continue;
            string key = f.Substring(4) + "|" + (int)kind;
            questKills[key] = (questKills.TryGetValue(key, out var n) ? n : 0) + 1;
        }
    }

    static string EnemyName(EnemyKind k) => k switch
    {
        EnemyKind.Crab => "ปู", EnemyKind.Snake => "งู", EnemyKind.Boar => "หมูป่า",
        EnemyKind.Mimic => "เห็ดหลอก", EnemyKind.SporeSpider => "แมงมุมสปอร์", EnemyKind.ShroomKing => "ราชาเห็ด",
        EnemyKind.Barnacle => "เพรียงคลาน", EnemyKind.HermitCrab => "ปูเสฉวนหอยยักษ์",
        EnemyKind.StormCloud => "ก้อนเมฆสายฟ้า", EnemyKind.ChimeBird => "นกกระดิ่งลม",
        EnemyKind.LanternFish => "ปลาตะเกียง", EnemyKind.StoneSquid => "หมึกหิน",
        EnemyKind.WindupDoll => "ตุ๊กตาไขลาน", EnemyKind.TimeBeetle => "ด้วงทรายเวลา",
        EnemyKind.Snowman => "มนุษย์หิมะละลาย", EnemyKind.Snowling => "ตัวหิมะจิ๋ว", EnemyKind.FrostBat => "ค้างคาวน้ำแข็ง",
        _ => k.ToString(),
    };

    // ------------------------------------------------------------------ the shipwright
    /// The next boat upgrade, asked for by this island's shipwright. Every island wants its own
    /// material, and a hand with its own trouble (creatures, a feast, a lost child, the king...).
    Quest BoatQuest()
    {
        int L = Mathf.Max(1, raftLevel), t = tier;
        var q = new Quest
        {
            id = $"boat{island}_{raftLevel}",
            title = $"อัปเกรดเรือเป็น Lv {raftLevel + 1}",
            rewardText = $"{BoatName(raftLevel + 1)} Lv {raftLevel + 1}",
            reward = () => { UpgradeRaft(); Hint($"ช่างต่อเรือ: เสร็จแล้ว! ตอนนี้เป็น{BoatName(raftLevel)} Lv {raftLevel} ไปเกาะใหม่ได้แล้ว", 6f); },
        };
        switch (biome)
        {
            case Biome.Mushroom:
                q.bring = new[] { (Item.GlowSpore, 6 + L), (Item.Wood, 6), (Item.Rope, 4) };
                q.help = new[] { ($"child{island}", "พาน้องปุยกลับไปหาหมอโมรา"), ($"king{island}", "ปราบราชาเห็ดที่ขวางทางน้ำ") };
                break;
            case Biome.Ice:
                q.bring = new[] { (Item.IceShard, 6 + L), (Item.Fur, 3 + L), (Item.Wood, 6), (Item.Rope, 4) };
                q.kill = new[] { (EnemyKind.FrostBat, 3 + t) };
                q.help = new[] { ($"warm{island}", "ช่วยพรานธาราจุดไฟให้หมู่บ้าน") };
                break;
            case Biome.Hourglass:
                q.bring = new[] { (Item.Gear, 4 + L), (Item.TimeSand, 6 + L), (Item.Wood, 4), (Item.Rope, 4) };
                q.kill = new[] { (EnemyKind.TimeBeetle, 3 + t) };
                q.help = new[] { ($"clock{island}", "ช่วยลุงฟันเฟืองไขลานหอนาฬิกา") };
                break;
            case Biome.Bubble:
                q.bring = new[] { (Item.Pearl, 3 + L), (Item.Coral, 6 + L), (Item.Wood, 4), (Item.Rope, 4) };
                q.kill = new[] { (EnemyKind.LanternFish, 3 + t) };
                q.help = new[] { ($"dome{island}", "ช่วยแม่มุกซ่อมโดมอากาศ") };
                break;
            case Biome.Sky:
                q.bring = new[] { (Item.CloudCotton, 6 + L), (Item.Feather, 3 + L), (Item.Wood, 4), (Item.Rope, 4) };
                q.kill = new[] { (EnemyKind.StormCloud, 2 + t) };
                q.help = new[] { ($"windcalm{island}", "ช่วยปู่ลมพัดตามหากระดิ่งลม") };
                break;
            case Biome.Turtle:
                q.bring = new[] { (Item.ShellShard, 6 + L), (Item.Wood, 6), (Item.Rope, 4) };
                q.kill = new[] { (EnemyKind.HermitCrab, 2 + t) };
                q.help = new[] { ($"turtlecalm{island}", "ช่วยยายชบาขัดเพรียงให้ท่านเต่า") };
                break;
            case Biome.Jungle:
                q.bring = new[] { (Item.Bamboo, 8 + 2 * L), (Item.Rope, 4 + L), (Item.Wood, 4) };
                q.kill = new[] { (EnemyKind.Snake, 3 + t) };
                break;
            case Biome.Tribal:
                q.bring = new[] { (Item.Cloth, 3 + L), (Item.Wood, 6 + L), (Item.Rope, 4), (Item.CookedFish, 2) };
                break;
            case Biome.Volcanic:
                q.bring = new[] { (Item.Obsidian, 4 + L), (Item.Wood, 6 + L), (Item.Rope, 4) };
                q.kill = new[] { (EnemyKind.Boar, 3 + t) };
                break;
            default:
                q.bring = new[] { (Item.Wood, 8 + 2 * L), (Item.Rope, 4 + L), (Item.Stone, 4) };
                q.kill = new[] { (EnemyKind.Crab, 2 + t) };
                break;
        }
        return q;
    }

    string ShipwrightTalk() => biome switch
    {
        Biome.Mushroom => "เรือแบบเจ้าแล่นไม่ผ่านคลองเห็ดหรอก ราชาเห็ดนอนขวางทางน้ำอยู่ แถมหมอโมราก็ร้อนใจเรื่องลูกหาย ช่วยเกาะนี้ก่อน แล้วข้าจะเสริมเรือให้ด้วยสปอร์เรืองแสง",
        Biome.Ice => "ทะเลรอบเกาะอุ่น แต่ท้องเรือต้องหุ้มเกล็ดน้ำแข็งกันความร้อนเวลาผ่านกระแสน้ำเดือด แปลกดีไหมล่ะ ค้างคาวน้ำแข็งชอบมาเกาะเสากระโดงแล้วแช่แข็งเชือก ไล่มันไป แล้วช่วยพรานธาราด้วย",
        Biome.Hourglass => "เวลาที่นี่เดี๋ยวเร็วเดี๋ยวช้า ไม้ที่ข้าตอกเมื่อเช้าผุไปแล้วตอนบ่าย! ต้องใช้เฟืองทองเหลืองกับทรายเวลายึดเรือให้ทนกาลเวลา ด้วงทรายชอบมาแทะเฟือง ช่วยไล่หน่อย แล้วช่วยลุงฟันเฟืองซ่อมหอนาฬิกาด้วย",
        Biome.Bubble => "จะพาเรือขึ้นจากใต้ทะเล ต้องหุ้มท้องเรือด้วยปะการังกับมุกให้ทนแรงดัน ปลาตะเกียงชอบล่อคนงานข้าหลงทาง ไล่พวกมันไป แล้วช่วยแม่มุกซ่อมโดมด้วย",
        Biome.Sky => "เรือที่จะออกจากเกาะลอยฟ้าต้องเบาดั่งปุยเมฆ เอาปุยเมฆกับขนนกมา แล้วจัดการก้อนเมฆสายฟ้าที่ชอบผ่าเรือข้า และช่วยปู่ลมพัดให้ลมสงบก่อนด้วย",
        Biome.Turtle => "เปลือกหอยยักษ์ที่นี่แข็งยิ่งกว่าหิน ใช้หุ้มท้องเรือกันคลื่นได้ แต่ปูเสฉวนหวงเปลือกนัก ต้องตีให้เปลือกแตกก่อน แล้วไปช่วยยายชบาด้วย ท่านเต่าดำน้ำบ่อยจนข้าต่อเรือไม่ได้",
        Biome.Jungle => "ไม้ไผ่ที่นี่เบาและเหนียว ทำตัวเรือได้ดีที่สุด แต่งูชุมจนข้าเข้าป่าไม่ได้ ช่วยไล่มันหน่อย",
        Biome.Tribal => "ใบเรือผ้าทอรับลมได้ดีนัก แต่หมู่บ้านกำลังจะจัดงานเลี้ยง เอาปลาย่างมาช่วยด้วยนะ",
        Biome.Volcanic => "ออบซิเดียนคมกริบ ทำหัวเรือฝ่าคลื่นได้ แต่หมูป่าดุเฝ้าลานลาวาอยู่ จัดการมันก่อน",
        _ => "ทะเลข้างหน้าคลื่นแรงขึ้นทุกเกาะ หาไม้กับเชือกมาให้ข้า แล้วไล่ปูที่มาแทะเรือตอนกลางคืนด้วย",
    };

    // ------------------------------------------------------------------ the mushroom doctor
    Quest[] DoctorQuests() => new[]
    {
        new Quest
        {
            id = $"lost{island}", title = "ตามหาน้องปุย",
            help = new[] { ($"child{island}", "หาน้องปุยในป่าเห็ด แล้วพากลับบ้าน") },
            rewardText = "เรียนทำยาแก้พิษ",
            reward = () => { learned.Add(Item.Antidote); AddXp(30, PlayerPos); TechPopup(Item.Antidote); },
        },
        new Quest
        {
            id = $"spore{island}", title = "สปอร์เรืองแสง",
            bring = new[] { (Item.GlowSpore, 5) },
            kill = new[] { (EnemyKind.SporeSpider, 3 + tier) },
            rewardText = "เรียนทำโคมเห็ด",
            reward = () => { learned.Add(Item.MushroomLantern); AddXp(40, PlayerPos); TechPopup(Item.MushroomLantern); },
        },
    };

    void TechPopup(Item it)
    {
        Popup($"เทคโนโลยีใหม่: {Name(it)}!", PlayerPos + Vector3.up * 1.1f, new Color(0.6f, 1f, 0.9f));
        Hint($"เรียนรู้{Name(it)}แล้ว! กด Tab เพื่อประดิษฐ์", 5f);
    }

    /// The quest this person has for you right now (null = nothing).
    Quest QuestOf(Villager v)
    {
        switch (v.role)
        {
            case VillagerRole.Shipwright:
                return raftLevel > 0 && raftLevel < farthest ? BoatQuest() : null;
            case VillagerRole.Doctor:
                foreach (var q in DoctorQuests()) if (!Done(q)) return q;
                return null;
            case VillagerRole.Keeper:
                var kq = KeeperQuest();
                return Done(kq) ? null : kq;
            case VillagerRole.Sage:
                var sq = SageQuest();
                return Done(sq) ? null : sq;
            case VillagerRole.Diver:
                var dq = DiverQuest();
                return Done(dq) ? null : dq;
            case VillagerRole.Clockmaker:
                var cq = ClockQuest();
                return Done(cq) ? null : cq;
            case VillagerRole.Hunter:
                var hq = HunterQuest();
                return Done(hq) ? null : hq;
            default: return null;
        }
    }

    /// Something new to say: an offer, or a quest ready to hand in ("!" over their head).
    public bool HasNews(Villager v)
    {
        if (v.role == VillagerRole.Weaver) return WeaverHasLessons;
        if (v.role == VillagerRole.Child) return !Flag("child" + island);
        var q = QuestOf(v);
        return q != null && (!Accepted(q) || QuestReady(q));
    }

    /// Dialog for people with quests. Returns false if this person isn't a quest giver.
    bool QuestDialog(Villager v, Dialog d, string text)
    {
        switch (v.role)
        {
            case VillagerRole.Child:
                if (!Flag("child" + island))
                {
                    d.text = text ?? "ฮือ... หนูเดินตามเห็ดเรืองแสงมาจนหลงทาง กลับบ้านไม่ถูกแล้ว พี่พาหนูกลับไปหาแม่ได้ไหม?";
                    d.options.Add(("ได้สิ ตามพี่มา (พากลับหมู่บ้าน)", () => BringChildHome(v), true));
                }
                else d.text = text ?? "ขอบคุณที่พาหนูกลับบ้านนะ! หนูจะไม่เดินตามเห็ดอีกแล้ว";
                return true;
            case VillagerRole.Shipwright:
            case VillagerRole.Doctor:
            case VillagerRole.Keeper:
            case VillagerRole.Sage:
            case VillagerRole.Diver:
            case VillagerRole.Clockmaker:
            case VillagerRole.Hunter:
                break;
            default:
                return false;
        }

        var q = QuestOf(v);
        if (q == null)
        {
            d.text = text ?? (v.role == VillagerRole.Shipwright
                ? (raftLevel == 0 ? "ยังไม่มีเรือเลยเหรอ? สร้างแพก่อน (Tab) แล้วข้าจะช่วยเสริมให้แข็งแรงขึ้น"
                                  : $"{BoatName(raftLevel)} Lv {raftLevel} ของเจ้าพร้อมไปเกาะใหม่แล้ว เมื่อไปถึงเกาะใหม่ ช่างที่นั่นจะช่วยเจ้าต่อ")
                : v.role == VillagerRole.Hunter ? "กองไฟใหญ่ลุกโชนแล้ว ทั้งหมู่บ้านอุ่นสบาย รองเท้าหนามคู่นั้นจะพาเจ้าเดินบนน้ำแข็งได้มั่นคง"
                : v.role == VillagerRole.Clockmaker ? "ติ๊กต่อก... ได้ยินไหม หอนาฬิกาเดินแล้ว! นาฬิกาแดดของเจ้าใช้หยุดเวลาได้เมื่อจวนตัว"
                : v.role == VillagerRole.Diver ? "โดมแน่นหนาแล้ว หายใจได้เต็มปอดเลย ระฆังดำน้ำจะพาเจ้าเก็บมุกในทะเลได้อีกมาก"
                : v.role == VillagerRole.Sage ? "ได้ยินไหม เสียงกระดิ่งกลับมาแล้ว ลมสงบลงเยอะเลย บอลลูนจะพาเรือเจ้าลอยพ้นปากฉลาม"
                : v.role == VillagerRole.Keeper ? "ท่านเต่าสบายตัวขึ้นมากแล้ว ขอบใจนะหลาน ใบเรือผ้าใบจะพาเจ้าแล่นได้เร็วดั่งลม"
                : "ขอบใจที่ช่วยเกาะเห็ดนะ ถ้าโดนพิษ ใช้ยาแก้พิษได้เลย และโคมเห็ดจะส่องทางให้ในคืนมืด");
            return true;
        }

        string intro = v.role == VillagerRole.Shipwright ? ShipwrightTalk()
                     : v.role == VillagerRole.Keeper ? KeeperIntro
                     : v.role == VillagerRole.Sage ? SageIntro
                     : v.role == VillagerRole.Diver ? DiverIntro
                     : v.role == VillagerRole.Clockmaker ? ClockIntro
                     : v.role == VillagerRole.Hunter ? HunterIntro
                     : q.id.StartsWith("lost") ? "ลูกสาวข้า น้องปุย หายเข้าไปในป่าเห็ดตั้งแต่เมื่อคืน! ป่ามีเห็ดหลอกกับแมงมุมสปอร์ ข้าออกไปเองไม่ได้ ช่วยตามหาที ข้าจะสอนวิธีทำยาแก้พิษให้"
                     : "แมงมุมสปอร์ทำรังใกล้หมู่บ้าน ช่วยไล่มันแล้วเก็บสปอร์เรืองแสงมาให้ข้า ข้าจะสอนทำโคมเห็ดที่ส่องสว่างได้ทั้งคืน";
        if (!Accepted(q))
        {
            d.text = text ?? $"{intro}\n<b>{q.title}</b>  รางวัล: {q.rewardText}\n{string.Join("\n", QuestLines(q))}";
            d.options.Add(($"รับภารกิจ: {q.title}", () => { flags.Add("acc:" + q.id); OpenDialog(v, $"ขอบใจ! ภารกิจ \"{q.title}\" ดูความคืบหน้าได้ที่มุมขวาบน"); }, true));
        }
        else
        {
            bool ready = QuestReady(q);
            d.text = text ?? $"<b>{q.title}</b>  รางวัล: {q.rewardText}\n{string.Join("\n", QuestLines(q))}";
            d.options.Add((ready ? $"ส่งภารกิจ: {q.title} ★" : "ยังทำไม่ครบ", () => { TurnIn(q); CloseDialog(); }, ready));
        }
        return true;
    }

    void BringChildHome(Villager child)
    {
        flags.Add("child" + island);
        var doctor = villagers.Find(x => x != null && x.role == VillagerRole.Doctor);
        Vector2 home = doctor != null ? doctor.grid + new Vector2(0.8f, 0.6f) : Center;
        Fx.Burst(child.transform.position + Vector3.up * 0.4f, new Color(0.6f, 1f, 0.9f), 14, 1f, 1.5f);
        child.Teleport(home);
        CloseDialog();
        Hint("น้องปุยกลับถึงบ้านแล้ว! กลับไปคุยกับหมอโมราได้เลย", 5f);
        Sfx.Play("levelup", 0.5f);
    }

    // ------------------------------------------------------------------ people on every island
    /// Everyone who lives on this island apart from the tribe (Tribal keeps its own village).
    void SpawnIslandPeople()
    {
        AddVillager(VillagerRole.Shipwright, "ช่างโป๊ะ", 4, ShipwrightSpot());
        if (biome == Biome.Mushroom)
        {
            var hut = nodes.Find(n => n != null && n.kind == NodeKind.Hut);
            Vector2 home = hut != null ? hut.grid + (Center - hut.grid).normalized * 1.4f : Center + new Vector2(1f, 1f);
            AddVillager(VillagerRole.Doctor, "หมอโมรา", 5, WalkableNear(home));
            Vector2 childAt = Flag("child" + island) ? WalkableNear(home + new Vector2(0.8f, 0.6f)) : FarFrom(ShipwrightSpot(), Center);
            AddVillager(VillagerRole.Child, "น้องปุย", 6, childAt);
        }
        if (biome == Biome.Turtle)
        {
            var hut = nodes.Find(n => n != null && n.kind == NodeKind.Hut);
            Vector2 home = hut != null ? hut.grid + (Center - hut.grid).normalized * 1.4f : Center + new Vector2(1f, 1f);
            AddVillager(VillagerRole.Keeper, "ยายชบา", 7, WalkableNear(home));
        }
        if (biome == Biome.Sky)
        {
            var hut = nodes.Find(n => n != null && n.kind == NodeKind.Hut);
            Vector2 home = hut != null ? hut.grid + (Center - hut.grid).normalized * 1.4f : Center + new Vector2(1f, 1f);
            AddVillager(VillagerRole.Sage, "ปู่ลมพัด", 8, WalkableNear(home));
        }
        if (biome == Biome.Bubble)
        {
            var hut = nodes.Find(n => n != null && n.kind == NodeKind.Hut);
            Vector2 home = hut != null ? hut.grid + (Center - hut.grid).normalized * 1.4f : Center + new Vector2(1f, 1f);
            AddVillager(VillagerRole.Diver, "แม่มุก", 12, WalkableNear(home));
        }
        if (biome == Biome.Hourglass)
        {
            var tower = nodes.Find(n => n != null && n.kind == NodeKind.ClockTower);
            Vector2 home = tower != null ? tower.grid + (Center - tower.grid).normalized * 1.6f : Center + new Vector2(1f, 1f);
            AddVillager(VillagerRole.Clockmaker, "ลุงฟันเฟือง", 13, WalkableNear(home));
        }
        if (biome == Biome.Ice)
        {
            var hut = nodes.Find(n => n != null && n.kind == NodeKind.Hut);
            Vector2 home = hut != null ? hut.grid + (Center - hut.grid).normalized * 1.4f : Center + new Vector2(1f, 1f);
            AddVillager(VillagerRole.Hunter, "พรานธารา", 14, WalkableNear(home));
        }
    }

    /// A couple of steps inland from the west shore, where the boat lands.
    Vector2 ShipwrightSpot()
    {
        Vector2? best = null;
        float bestScore = float.MaxValue;
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
            {
                if (tiles[x, y] != 1 && tiles[x, y] != 2) continue;
                var g = new Vector2(x, y);
                if (!TouchesSea(g)) continue;
                float score = (x - y) + Mathf.Abs(x + y - (Size - 1)) * 0.5f;
                if (score < bestScore) { bestScore = score; best = g; }
            }
        Vector2 shore = best ?? Center;
        return WalkableNear(shore + (Center - shore).normalized * 2.6f + new Vector2(0.9f, -0.9f));
    }

    Vector2 WalkableNear(Vector2 p)
    {
        if (CanWalk(p, 0.15f)) return p;
        for (int r = 1; r <= 6; r++)
            for (int a = 0; a < 12; a++)
            {
                var q = p + new Vector2(Mathf.Cos(a * 0.52f), Mathf.Sin(a * 0.52f)) * r * 0.6f;
                if (CanWalk(q, 0.15f)) return q;
            }
        return Center;
    }

    /// Deep in the woods: the grass spot furthest from all the given places (the landing,
    /// the village...). Same answer every time for the same island.
    Vector2 FarFrom(params Vector2[] away)
    {
        var r = new System.Random(seed ^ 0x5eed);
        Vector2 best = Center;
        float bestD = 0f;
        for (int i = 0; i < 500; i++)
        {
            var g = new Vector2(r.Next(Size), r.Next(Size));
            if (tiles[(int)g.x, (int)g.y] != 2 || !CanWalk(g, 0.15f)) continue;
            float d = float.MaxValue;
            foreach (var a in away) d = Mathf.Min(d, (g - a).magnitude);
            if (d > bestD) { bestD = d; best = g; }
        }
        return best;
    }

    /// The doctor's hut and fire in the middle of a freshly made Mushroom island.
    void BuildMushroomCamp()
    {
        Vector2 c = Center;
        foreach (var n in nodes.FindAll(n => (n.grid - c).magnitude < 3.6f)) RemoveNode(n);
        Spawn(NodeKind.Campfire, c, false);
        Spawn(NodeKind.Hut, c + new Vector2(2.2f, -1.2f), false);
    }

    // ------------------------------------------------------------------ boss
    /// The Mushroom King waits on his island until someone beats him.
    void SpawnBoss()
    {
        if (biome != Biome.Mushroom || Flag("king" + island) || !IsHost) return;
        if (enemies.Exists(e => e != null && e.kind == EnemyKind.ShroomKing)) return;
        // far from the landing, the village and where the child got lost
        Vector2 at = FarFrom(ShipwrightSpot(), Center, FarFrom(ShipwrightSpot(), Center));
        var e = new GameObject("ShroomKing").AddComponent<Enemy>();
        e.transform.SetParent(world);
        e.Init(EnemyKind.ShroomKing, at, tier, ++enemyCounter);
        enemies.Add(e);
    }

    // ------------------------------------------------------------------ poison, lantern, spores
    [System.NonSerialized] public float poisonT;
    Light2D lanternLight;
    readonly List<SporeCloud> clouds = new List<SporeCloud>();

    public void Poison(float seconds)
    {
        if (dead || Busy || Bubbled || Invisible && seconds < 4f) return;
        if (poisonT <= 0f) Popup("ติดพิษ!", PlayerPos + Vector3.up * 1f, new Color(0.6f, 1f, 0.4f));
        poisonT = Mathf.Max(poisonT, seconds);
    }

    void UseAntidote()
    {
        if (Count(Item.Antidote) <= 0) return;
        if (poisonT <= 0f) { Hint("ยังไม่ได้ติดพิษ เก็บยาไว้ก่อน", 2f); return; }
        inv[Item.Antidote]--;
        poisonT = 0f;
        Sfx.Play("drink", 0.6f);
        Popup("หายพิษแล้ว", PlayerPos + Vector3.up * 0.9f, new Color(0.6f, 1f, 0.9f));
    }

    public void SpawnSporeCloud(Vector2 g)
    {
        clouds.RemoveAll(c => c == null);
        if (clouds.Count >= 14) return;
        var c = new GameObject("Spores").AddComponent<SporeCloud>();
        c.transform.SetParent(world);
        c.Init(g);
        clouds.Add(c);
    }

    /// Owning a mushroom lantern lights up the dark around you.
    void UpdateLantern()
    {
        bool want = player != null && Count(Item.MushroomLantern) > 0;
        if (!want) { if (lanternLight != null) lanternLight.enabled = false; return; }
        if (lanternLight == null || lanternLight.transform.parent != player.transform)
        {
            lanternLight = CreateLight("Lantern", Light2D.LightType.Point, player.transform);
            lanternLight.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            lanternLight.color = new Color(0.45f, 1f, 0.9f);
            lanternLight.pointLightInnerRadius = 0.5f;
            lanternLight.pointLightOuterRadius = 4.5f;
        }
        lanternLight.enabled = true;
        lanternLight.intensity = Mathf.Lerp(1.3f, 0f, daylight);
    }

    // ------------------------------------------------------------------ save
    void SaveQuests(SaveData d)
    {
        d.flags.AddRange(flags);
        foreach (var kv in questKills) d.questKills.Add(kv.Key + "|" + kv.Value);
    }

    void LoadQuests(SaveData d)
    {
        flags.Clear();
        questKills.Clear();
        foreach (var f in d.flags) flags.Add(f);
        foreach (var s in d.questKills)
        {
            int cut = s.LastIndexOf('|');
            if (cut > 0 && int.TryParse(s.Substring(cut + 1), out var n)) questKills[s.Substring(0, cut)] = n;
        }
    }

    // ------------------------------------------------------------------ HUD
    /// Accepted quests on this island, top right.
    void DrawQuestTracker(float W)
    {
        if (voyage != null || craftOpen) return;
        float y = 80f;
        var st = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
        foreach (var v in villagers)
        {
            if (v == null) continue;
            var q = QuestOf(v);
            if (q == null || !Accepted(q)) continue;
            ShadowLabel(new Rect(W - 330, y, 316, 18), $"<b>{q.title}</b>{(QuestReady(q) ? "  <color=#ffd84d>ส่งได้แล้ว!</color>" : "")}", st, new Color(1f, 0.9f, 0.6f));
            y += 16f;
            foreach (var line in QuestLines(q))
            {
                ShadowLabel(new Rect(W - 330, y, 316, 18), line, st, line.StartsWith("✓") ? new Color(0.7f, 1f, 0.7f) : Color.white);
                y += 15f;
            }
            y += 6f;
        }
    }
}

/// A patch of poisonous spores left by spore spiders and the Mushroom King.
public class SporeCloud : MonoBehaviour
{
    Vector2 grid;
    float life = 7f, t;
    SpriteRenderer sr;

    public void Init(Vector2 g)
    {
        grid = g;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = PixelArt.SporeCloud();
        Vector3 flat = IslandGame.GridToWorld(g);
        transform.position = flat + Vector3.up * IslandGame.I.HeightAt(g);
        sr.sortingOrder = IslandGame.SortOrder(flat.y) - 5;
    }

    void Update()
    {
        t += Time.deltaTime;
        float a = Mathf.Clamp01(t / 0.5f) * Mathf.Clamp01((life - t) / 1.2f);
        sr.color = new Color(1f, 1f, 1f, a * (0.75f + 0.2f * Mathf.Sin(t * 5f)));
        var game = IslandGame.I;
        if (game.player != null && (game.player.grid - grid).magnitude < 0.75f) game.Poison(3f);
        if (t >= life) Destroy(gameObject);
    }
}
