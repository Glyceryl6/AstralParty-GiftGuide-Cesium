using System;
using System.Collections.Generic;
using GameLogic;
using UI;

namespace AstralProbe.Features.Inventory {
    internal readonly struct ContractRequirement {
        internal readonly int ItemId;
        internal readonly int Owned;
        internal readonly int Required;

        internal ContractRequirement(int itemId, int owned, int required) {
            ItemId = itemId;
            Owned = owned;
            Required = required;
        }
    }

    internal sealed class CharacterMaterialUsage {
        internal int HeroId;
        internal int SortOrder;
        internal string Name = "";
        internal string Title = "";
        internal bool ContractSigned;
        internal string Portrait = "";
        internal string HeroPortrait = "";
        internal int Quality;
        internal bool IsLinkage;
        internal bool IsUltimate;
        internal bool Owned;
        internal bool GiftPreferred;
        internal int FavorLevel;
        internal int FavorExp;
        internal int FavorNeedExp;
        internal bool FavorDeepened;
        internal int PotentialLevel;
        internal readonly List<ContractRequirement> ContractRequirements = new();
        internal bool ContractMaterial;
        internal int ContractMissing;
        internal bool PotentialMaterial;
        internal bool PotentialAwakened;
        internal int PotentialMissing;
        // The actual HeroPanel talent material cells, not a 'materials ready' label.
        internal readonly List<ContractRequirement> PotentialRequirements = new();
    }

    /// <summary>
    /// Reads the currently loaded AstralParty.Runtime objects directly. These
    /// are HybridCLR managed hotfix classes, not Il2CppInterop proxies.
    /// No cache of HeroCardData or static tables is kept between sessions.
    /// </summary>
    internal static class CharacterMaterialUsageReader {
        internal static bool TryRead(int itemId, out List<CharacterMaterialUsage> result) {
            result = new List<CharacterMaterialUsage>();
            if (itemId <= 0) return true;
            if (!GameLogicManager.hasInstance) return false;

            GameLogicManager manager = GameLogicManager.inst;
            HeroCardLogic heroCards = manager.heroCard;
            BagLogic bag = manager.bag;
            CharacterConfigure characters = StaticConfigure.Character;
            FavorConfigure favors = StaticConfigure.Favor;
            PVENurturanceConfigure potentials = StaticConfigure.PVENurturance;
            if (heroCards == null || bag == null || characters == null || favors == null || potentials == null) return false;

            int inBag = Math.Max(0, bag.GetItemCount(itemId));
            // Il2CppInterop-generated Protobuf collections are not guaranteed to
            // implement System.Collections.Generic.IEnumerable<T>. Use indexed
            // access for both native proxies and genuine HybridCLR collections.
            var infos = characters.Infos;
            if (infos == null) return false;
            int count = Math.Min(infos.Count, 512);
            for (int i = 0; i < count; i++) {
                CharacterInfoConfigure? info = infos[i];
                if (info == null || info.HeroType != CharacterType.Hero || info.Id <= 0) continue;

                HeroCardData? card = heroCards.GetCardData(info.Id);
                SkinStandingPaintingConfigureItem? painting = card?.standingPainting;
                if (painting == null) painting = heroCards.GetConfigStandingPainting(info.Id, 0, 0);
                var usage = new CharacterMaterialUsage {
                    HeroId = info.Id,
                    SortOrder = info.OrderWeight,
                    Name = LocalName(info.NameID, info.Id),
                    Title = info.NickID > 0 ? LocalTitle(info.NickID) : string.Empty,
                    ContractSigned = card?.isBreakThrough == true,
                    // ProfilePhoto is the game's square-ish head icon used in
                    // battle HUD and chat. RolePhoto is a wide standing-art
                    // capture and must not be used as the default avatar.
                    Portrait = painting == null ? string.Empty :
                        (!string.IsNullOrWhiteSpace(painting.ProfilePhoto)
                            ? painting.ProfilePhoto : painting.GetCharacterPhoto()),
                    HeroPortrait = painting?.GetCharacterPhoto() ?? string.Empty,
                    Quality = painting == null ? 0 : (int)painting.SkinAppearanceType,
                    IsLinkage = info.IsLinkage,
                    IsUltimate = painting?.SkinAppearanceType == SkinAppearanceType.Ultimate,
                    Owned = card?.IsHas ?? false,
                    FavorLevel = card?.LV ?? 0,
                    FavorExp = Math.Max(0, card?.Exp ?? 0),
                    PotentialLevel = card?.PveData?.Level ?? 0
                };

                if (favors.LevelDict != null && favors.LevelDict.TryGetValue(
                    usage.FavorLevel, out FavorLevelConfigure currentFavor) && currentFavor != null)
                    usage.FavorNeedExp = Math.Max(0, currentFavor.NeedExp);
                int maxFavorLevel = favors.Levels != null && favors.Levels.Count > 0
                    ? favors.Levels[favors.Levels.Count - 1].Id : 5;
                usage.FavorDeepened = info.HasKizuna && card != null &&
                    card.isBreakThrough && (card.LV >= maxFavorLevel ||
                        (usage.FavorNeedExp > 0 && card.Exp >= usage.FavorNeedExp));

                var giftDict = characters.HeroFavorGiftDict;
                if (giftDict != null && giftDict.TryGetValue(info.HeroFavorGift,
                    out CharacterHeroFavorGiftConfigure giftConfig) && giftConfig != null) {
                    var gifts = giftConfig.CharacterHeroFavorGiftConfigureItems;
                    if (gifts != null) {
                        for (int j = 0; j < gifts.Count; j++) {
                            if (gifts[j] != null && gifts[j].ItemID == itemId) {
                                usage.GiftPreferred = true;
                                break;
                            }
                        }
                    }
                }

                var breakthroughDict = favors.BreakthroughDict;
                if (breakthroughDict != null && breakthroughDict.TryGetValue(
                    info.FavorBreakthroughReward, out FavorBreakthroughConfigure contract) && contract != null) {
                    int need = Need(contract.NeedMaterials, itemId);
                    usage.ContractMaterial = need > 0;
                    usage.ContractMissing = usage.ContractSigned ? 0 : Math.Max(0, need - inBag);
                    if (usage.ContractMaterial && contract.NeedMaterials != null) {
                        // The native HeroPanel displays the full material list,
                        // not only the item currently being inspected.
                        foreach (KeyValuePair<int, int> material in contract.NeedMaterials) {
                            if (material.Key <= 0 || material.Value <= 0) continue;
                            usage.ContractRequirements.Add(new ContractRequirement(material.Key,
                                Math.Max(0, bag.GetItemCount(material.Key)), material.Value));
                        }
                    }
                }

                var breaks = info.PveBreak;
                var breakDict = potentials.BreakDict;
                PVENurturanceBreakConfigure? current = card?.PveData?.GetCurrentTalentConfigure();
                PVENurturanceBreakConfigure? matchedTalent = null;
                if (breaks != null && breakDict != null) {
                    for (int j = 0; j < breaks.Count; j++) {
                        if (!breakDict.TryGetValue(breaks[j], out PVENurturanceBreakConfigure talent) ||
                            talent == null) continue;
                        int need = Need(talent.NeedMaterials, itemId);
                        if (need <= 0) continue;
                        usage.PotentialMaterial = true;
                        if (card?.PveData?.IsTalentUnlock(talent.Id) != true)
                            usage.PotentialMissing += need;
                        // Prefer the same active talent used by HeroPanel.
                        // Older materials can still belong to a prior talent;
                        // choose the first matching one if there is no active match.
                        if (matchedTalent == null || (current != null && current.Id == talent.Id))
                            matchedTalent = talent;
                    }
                }
                if (matchedTalent?.NeedMaterials != null)
                    foreach (KeyValuePair<int, int> material in matchedTalent.NeedMaterials) {
                        if (material.Key <= 0 || material.Value <= 0) continue;
                        usage.PotentialRequirements.Add(new ContractRequirement(material.Key,
                            Math.Max(0, bag.GetItemCount(material.Key)), material.Value));
                    }
                usage.PotentialAwakened = current != null &&
                    card?.PveData?.IsTalentUnlock(current.Id) == true;
                usage.PotentialMissing = usage.PotentialAwakened ? 0 : Math.Max(0, usage.PotentialMissing - inBag);

                if (usage.GiftPreferred || usage.ContractMaterial || usage.PotentialMaterial) result.Add(usage);
            }
            result.Sort((a, b) => a.SortOrder != b.SortOrder
                ? a.SortOrder.CompareTo(b.SortOrder) : a.HeroId.CompareTo(b.HeroId));
            return true;
        }

        private static int Need(Google.Protobuf.Collections.MapField<int, int> materials, int itemId) {
            return materials != null && materials.TryGetValue(itemId, out int count) ? count : 0;
        }

        private static string LocalTitle(int textId) {
            if (textId <= 0 || StaticConfigure.STRCharacter?.LocalDict == null ||
                !StaticConfigure.STRCharacter.LocalDict.ContainsKey(textId)) return string.Empty;
            return textId.GetLocal(UIStringType.Character) ?? string.Empty;
        }

        private static string LocalName(int textId, int heroId) {
            if (textId <= 0 || StaticConfigure.STRCharacter?.LocalDict == null ||
                !StaticConfigure.STRCharacter.LocalDict.ContainsKey(textId)) return "#" + heroId;
            string localized = textId.GetLocal(UIStringType.Character);
            return string.IsNullOrWhiteSpace(localized) ? "#" + heroId : localized;
        }
    }
}
