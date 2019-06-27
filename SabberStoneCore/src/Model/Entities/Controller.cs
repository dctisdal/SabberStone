﻿#region copyright
// SabberStone, Hearthstone Simulator in C# .NET Core
// Copyright (C) 2017-2019 SabberStone Team, darkfriend77 & rnilva
//
// SabberStone is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License.
// SabberStone is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Loader;
using SabberStoneCore.Tasks.PlayerTasks;
using SabberStoneCore.Model.Zones;

namespace SabberStoneCore.Model.Entities
{
	/// <summary>
	/// Instance that represents a player in SabberStone game instances.
	/// <seealso cref="Entity"/>
	/// </summary>
	public partial class Controller : Entity
	{
		private readonly int _playerId;

		/// <summary>
		/// Available zones for this player.
		/// </summary>
		public ControlledZones ControlledZones;

		/// <summary>
		/// The deck of this player.
		/// This zone contains cards which are not yet drawn. Can be empty.
		/// </summary>
		public DeckZone DeckZone;

		/// <summary>
		/// The hand of this player.
		/// This zone contains cards which were drawn from deck or generated.
		/// during the game. Can be empty.
		/// </summary>
		public HandZone HandZone;

		/// <summary>
		/// The minions friendly to this player.
		/// This zone contains all minion entities generated by cards from the hand.
		/// Can be empty.
		/// </summary>
		public BoardZone BoardZone;

		/// <summary>
		/// The zone containing all entities which were once in play, but now destroyed. Can be empty.
		/// </summary>
		public GraveyardZone GraveyardZone;

		/// <summary>
		/// The zone containing all played secrets by this player. Can be empty.
		/// </summary>
		public SecretZone SecretZone;

		/// <summary>
		/// The zone containing all entities that need to be chosen by the player.
		/// Before an option set is created, it's entities are built and stored in the this zone.
		/// The picked entity will move from that zone into the hand zone.
		///
		/// Unpicked entities will remain in the setaside zone.
		/// </summary>
		public SetasideZone SetasideZone;

		public readonly ControllerAuraEffects ControllerAuraEffects;

		public readonly List<int> DiscardedEntities;


		public readonly List<Card> CardsPlayedThisTurn;
		public readonly List<PlayHistoryEntry> PlayHistory;

		/// <summary>
		/// Name of the player.
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// Initial cards that are in the deck of the controller.
		/// </summary>
		public List<Card> DeckCards { get; internal set; } = new List<Card>();

		/// <summary>
		/// Base class of the controller.
		/// </summary>
		public CardClass BaseClass { get; internal set; }


		/// <summary>
		/// The hero entity representing this player.
		/// </summary>
		public Hero Hero { get; set; }

		/// <summary>
		/// The cardclass of the deck.
		/// Only neutral cards and cards specific to this class are allowed in the starting deck
		/// of the player.
		/// </summary>
		public CardClass HeroClass => Hero.Card.Class;

		/// <summary>
		/// All standard cards which can be put into a deck of this class.
		/// </summary>
		public IReadOnlyList<Card> Standard => Cards.Standard[HeroClass];

		/// <summary>
		/// All wild cards which can be put into a deck of this class.
		/// </summary>
		public IReadOnlyList<Card> Wild => Cards.Wild[HeroClass];

		/// <summary>
		/// The amount of mana available to actually use after calculating all resource factors.
		/// </summary>
		public int RemainingMana => BaseMana + TemporaryMana - (UsedMana + OverloadLocked);

		/// <summary>
		/// Returns true if this player has a dragon in his hand.
		/// </summary>
		public bool DragonInHand => HandZone.Any(p => p.Card.IsRace(Race.DRAGON));

		public int NumTotemSummonedThisGame { get; set; }

		public bool TemporusFlag { get; set; }

		/// <summary>
		/// The last choice set proposed to this player.
		/// The actual chosen entity is also stored in the Choice object.
		/// </summary>
		public Choice Choice { get; set; }

		/// <summary>
		/// The opponent player instance.
		/// </summary>
		public Controller Opponent => _opponent ?? (_opponent = Game.Player1 == this ? Game.Player2 : Game.Player1);

		public override int this[GameTag t]
		{
			get
			{
				_data.TryGetValue(t, out int value);
				return value + ControllerAuraEffects[t];
			}
			set
			{
				if (_logging)
					Game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "Entity", !Game.Logging ? "" : $"{this} set data {t} to {value}");
				if (_history && (int)t < 1000)
					if (value + ControllerAuraEffects[t] != this[t])
						Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, t, value));

				_data[t] = value;
			}
		}

		/// <summary>
		/// Create a new controller instance.
		/// </summary>
		/// <param name="game">The game to which it registers.</param>
		/// <param name="name">The name of the player.</param>
		/// <param name="playerId">The player index; The first player will get assigned 1.</param>
		/// <param name="id">Entity ID of this controller.</param>
		public Controller(Game game, string name, int playerId, int id, IDictionary<GameTag, int> tags)
			: base(in game, Card.CardPlayer, in tags, in id)
		{
			Name = name;
			_playerId = playerId;
			Controller = this;

			DeckZone = new DeckZone(this);
			BoardZone = new BoardZone(this);
			HandZone = new HandZone(this);
			SecretZone = new SecretZone(this);
			GraveyardZone = new GraveyardZone(this);
			SetasideZone = new SetasideZone(this);

			ControlledZones = new ControlledZones(this);

			ControllerAuraEffects = new ControllerAuraEffects();

			DiscardedEntities = new List<int>();
			CardsPlayedThisTurn = new List<Card>(10);
			//cardsPlayedThisGame = new List<Card>(30);
			PlayHistory = new List<PlayHistoryEntry>(30);
		
			Game.Log(LogLevel.INFO, BlockType.PLAY, "Controller", !Game.Logging? "":$"Created Controller '{name}'");
		}

		/// <summary>
		/// A copy constructor.
		/// </summary>
		/// <param name="game">The target <see cref="Game"/> instance.</param>
		/// <param name="controller">The source <see cref="Controller"/></param>
		private Controller(in Game game, in Controller controller) : base(in game, controller)
		{
			Name = controller.Name;

			Controller = this;

			Hero = (Hero)controller.Hero.Clone(this);

			Hero.HeroPower = (HeroPower)controller.Hero.HeroPower.Clone(this);

			if (controller.Hero.Weapon != null)
			{
				Hero.Weapon = (Weapon)controller.Hero.Weapon.Clone(this);
			}

			Choice = controller.Choice?.Clone(this);

			BoardZone = new BoardZone(this);
			SetasideZone = controller.SetasideZone.Clone(this);
			DeckZone = controller.DeckZone.Clone(this);
			HandZone = controller.HandZone.Clone(this);
			GraveyardZone = controller.GraveyardZone.Clone(this);
			SecretZone = controller.SecretZone.Clone(this);
			controller.BoardZone.Stamp(BoardZone);

			DeckCards = controller.DeckCards;
			BaseClass = controller.BaseClass;

			ControlledZones = new ControlledZones(this);
			ControllerAuraEffects = controller.ControllerAuraEffects.Clone();

			PlayHistory = new List<PlayHistoryEntry>(controller.PlayHistory);
			DiscardedEntities = new List<int>(controller.DiscardedEntities);
			CardsPlayedThisTurn = new List<Card>(controller.CardsPlayedThisTurn);

			// Cloning applied enchantments.
			{
				List<Enchantment> originalEnchantments = controller.AppliedEnchantments;
				if (originalEnchantments != null)
				{
					var enchantments = new List<Enchantment>(originalEnchantments.Count);
					foreach (Enchantment p in originalEnchantments)
					{
						enchantments.Add(p.Clone(this));
					}
					AppliedEnchantments = enchantments;
				}
			}

			// non-tag attributes
			_playerId = controller._playerId;
			_currentSpellPower = controller._currentSpellPower;
			NumTotemSummonedThisGame = controller.NumTotemSummonedThisGame;
			TemporusFlag = controller.TemporusFlag;
		}

		/// <summary>
		/// Performs a deep copy of this <see cref="Controller"/> instance and returns the result.
		/// Copied instance and all entities in its zones are deep copied to the target <see cref="Game"/> instance.
		/// </summary>
		/// <param name="game">The target Game.</param>
		/// <returns></returns>
		public Controller Clone(in Game game)
		{
			return new Controller(in game, this);
		}

		/// <summary>
		/// Adds a new Hero entity and HeroPower entity to the game instance.
		/// </summary>
		/// <param name="heroCard">The card to derive the hero entity from.</param>
		/// <param name="powerCard">The heropower card to derive the hero power entity from.</param>
		/// <param name="tags">The inherited tags</param>
		/// <param name="id">The entity id to assign to the generated HERO entity</param>
		public void AddHeroAndPower(in Card heroCard, in Card powerCard = null, in IDictionary<GameTag, int> tags = null, in int id = -1)
		{
			// remove hero and place it to the setaside zone
			Weapon weapon = null;
			AuraEffects auraEffects = null;
			if  (Hero != null)
			{
				SetasideZone.MoveTo(Hero, SetasideZone.Count);
				SetasideZone.MoveTo(Hero.HeroPower, SetasideZone.Count);
				//Hero[GameTag.EXHAUSTED] = 0;
				//Hero[GameTag.NUM_ATTACKS_THIS_TURN ] = 0;
				//Hero[GameTag.DAMAGE] = 0;
				//Hero[GameTag.REVEALED] = 1;
				if (Hero.Weapon != null)
				{
					weapon = Hero.Weapon;
				}

				auraEffects = Hero.AuraEffects;
			}


			Hero = (Hero) FromCard(this, in heroCard, tags, null, id);
			Hero[GameTag.ZONE] = (int) Enums.Zone.PLAY;
			HeroId = Hero.Id;
			Hero.HeroPower = FromCard(this, powerCard ?? Cards.FromAssetId(Hero[GameTag.HERO_POWER]),
				new EntityData { [GameTag.CREATOR] = Hero.Id }) as HeroPower;
			Hero.Weapon = weapon;
			Hero.AuraEffects = auraEffects;
		}

		/// <summary>
		/// Build a unique string describing this object.
		/// </summary>
		/// <param name="ignore">All GameTags which have to be ignored during hash generation.</param>
		/// <returns>The unique hash string.</returns>
		public override string Hash(params GameTag[] ignore)
		{
			var str = new StringBuilder();
			str.Append("[C:");
			str.Append(Name);
			str.Append("]");
			str.Append(base.Hash(ignore));
			str.Append(ControllerAuraEffects.Hash());
			str.Append(Hero.Hash(ignore));
			str.Append(Hero.HeroPower.Hash(ignore));
			if (Hero.Weapon != null)
				str.Append(Hero.Weapon.Hash(ignore));
			str.Append(ControlledZones.Hash(ignore));
			return str.ToString();
		}

		/// <summary>
		/// Returns a set of all options this player can perform execute at the moment.
		/// From this set one option is picked and executed by the game.
		/// </summary>
		/// <param name="skipPrePhase">Doesn't check validity of the options generated from this when it is processed.</param>
		/// <returns></returns>
		public List<PlayerTask> Options(bool skipPrePhase = true)
		{
			// No options for the opponent player.
			if (this != Game.CurrentPlayer)
				return new List<PlayerTask>(0);

			//	ChooseTasks
			if (Choice != null)
			{
				switch (Choice.ChoiceType)
				{
					case ChoiceType.GENERAL:
						return Choice.Choices.Select(p => (PlayerTask)ChooseTask.Pick(this, p)).ToList();

					case ChoiceType.MULLIGAN:
						IEnumerable<IEnumerable<int>> choices = Util.GetPowerSet(Choice.Choices);
						return choices.Select(p => (PlayerTask) ChooseTask.Mulligan(this, p.ToList())).ToList();

					default:
						throw new NotImplementedException();
				}
			}

			// no options till mulligan is done for both players
			if (Game.Step != Step.MAIN_ACTION)
				return new List<PlayerTask>(0);

			//	EndTurnTask
			var allOptions = new List<PlayerTask>(20) { EndTurnTask.Any(this) };

			#region PlayCardTasks
			int mana = RemainingMana;
			int zonePosRange = BoardZone.Count;
			bool? spellCostHealth = null;

			Character[] allTargets = null;
			Minion[] friendlyMinions = null;
			Minion[] enemyMinions = null;
			Minion[] allMinions = null;
			Character[] allFriendly = null;
			Character[] allEnemies = null;

			var handSpan = HandZone.GetSpan();
			for (int i = 0; i < handSpan.Length; i++)
			{
				if (!handSpan[i].ChooseOne || ChooseBoth)
					GetPlayCardTasks(handSpan[i]);
				else
				{
					IPlayable[] playables = handSpan[i].ChooseOnePlayables;
					for (int j = 1; j < 3; j++)
						GetPlayCardTasks(handSpan[i], playables[j - 1], j);
				}
			}
			#endregion

			#region HeroPowerTask
			HeroPower power = Hero.HeroPower;
			Card heroPowerCard = power.Card;
			if (!power.IsExhausted && mana >= power.Cost &&
			    !HeroPowerDisabled && !heroPowerCard.HideStat)
			{
				if (heroPowerCard.ChooseOne)
				{
					if (ChooseBoth)
						allOptions.Add(HeroPowerTask.Any(this, skipPrePhase: true));
					else
					{
						allOptions.Add(HeroPowerTask.Any(this, null, 1, true));
						allOptions.Add(HeroPowerTask.Any(this, null, 2, true));
					}
				}
				else
				{
					if (heroPowerCard.IsPlayableByCardReq(this))
					{
						Character[] targets = GetTargets(heroPowerCard);
						if (targets != null)
							for (int i = 0; i < targets.Length; i++)
								allOptions.Add(HeroPowerTask.Any(this, targets[i], skipPrePhase: true));
						else
							allOptions.Add(HeroPowerTask.Any(this, skipPrePhase: true));
					}
				}
			}
			#endregion

			#region MinionAttackTasks
			Minion[] attackTargets = null;
			bool isOpHeroValidAttackTarget = false;
			var boardSpan = BoardZone.GetSpan();
			for (int j = 0; j < boardSpan.Length; j++)
			{
				Minion minion = boardSpan[j];

				if (minion.IsExhausted && (!minion.HasCharge || minion.NumAttacksThisTurn != 0))
					continue;
				if (minion.IsFrozen || minion.AttackDamage == 0 || minion.CantAttack || minion.Untouchable)
					continue;

				GenerateAttackTargets();

				for (int i = 0; i < attackTargets.Length; i++)
					allOptions.Add(MinionAttackTask.Any(this, minion, attackTargets[i], skipPrePhase));

				if (isOpHeroValidAttackTarget && !(minion.CantAttackHeroes || minion.AttackableByRush))
					allOptions.Add(MinionAttackTask.Any(this, minion, Opponent.Hero, skipPrePhase));
			}
			#endregion

			#region HeroAttackTaskts
			Hero hero = Hero;

			if ((!hero.IsExhausted || (hero.ExtraAttacksThisTurn > 0 && hero.ExtraAttacksThisTurn >= hero.NumAttacksThisTurn))
			    && hero.AttackDamage > 0 && !hero.IsFrozen)
			{
				GenerateAttackTargets();

				for (int i = 0; i < attackTargets.Length; i++)
					allOptions.Add(HeroAttackTask.Any(this, attackTargets[i], skipPrePhase));

				if (isOpHeroValidAttackTarget && !hero.CantAttackHeroes)
					allOptions.Add(HeroAttackTask.Any(this, Opponent.Hero, skipPrePhase));
			}
			#endregion

			return allOptions;

			#region local functions
			void GetPlayCardTasks(in IPlayable playable, in IPlayable chooseOnePlayable = null, int subOption = -1)
			{
				Card card = chooseOnePlayable?.Card ?? playable.Card;

				if (!spellCostHealth.HasValue)
					spellCostHealth = ControllerAuraEffects[GameTag.SPELLS_COST_HEALTH] == 1;

				bool healthCost = (playable.AuraEffects?.CardCostHealth ?? false) ||
				                  (spellCostHealth.Value && playable.Card.Type == CardType.SPELL);

				if (!healthCost && (playable.Cost > mana || playable.Card.HideStat))
					return;

				// check PlayableByPlayer
				switch (playable.Card.Type)
				{
					//	REQ_MINION_CAP
					case CardType.MINION when BoardZone.IsFull:
						return;
					case CardType.SPELL:
					{
						if (card.IsSecret)
						{
							if (SecretZone.IsFull) // REQ_SECRET_CAP
								return;
							if (SecretZone.Any(p => p.Card.AssetId == card.AssetId)) // REQ_UNIQUE_SECRET
								return;
						}

						if (card.IsQuest && SecretZone.Quest != null)
							return;
						break;
					}
				}

				{
					if (!card.IsPlayableByCardReq(this))
						return;

					Character[] targets = GetTargets(card);

					// Card doesn't require any targets
					if (targets == null)
					{
						if (playable is Minion)
							for (int i = 0; i <= zonePosRange; i++)
								allOptions.Add(PlayCardTask.Any(this, playable, null, i, subOption, skipPrePhase));
						else
							allOptions.Add(PlayCardTask.Any(this, playable, null, -1, subOption, skipPrePhase));
					}
					else
					{
						if (targets.Length == 0)
						{
							if (card.MustHaveTargetToPlay)
								return;

							if (playable is Minion)
								for (int i = 0; i <= zonePosRange; i++)
									allOptions.Add(PlayCardTask.Any(this, playable, null, i, subOption, skipPrePhase));
							else
								allOptions.Add(PlayCardTask.Any(this, playable, null, -1, subOption, skipPrePhase));
						}
						else
						{
							for (int j = 0; j < targets.Length; j++)
							{
								ICharacter target = targets[j];
								if (playable is Minion)
									for (int i = 0; i <= zonePosRange; i++)
										allOptions.Add(PlayCardTask.Any(this, playable, target, i, subOption,
											true));
								else
									allOptions.Add(PlayCardTask.Any(this, playable, target, -1, subOption, skipPrePhase));

							}
						}
					}
				}
			}

			// Returns null if targeting is not required
			// Returns 0 Array if there is no available target
			Character[] GetTargets(Card card)
			{
				// Check it needs additional validation
				if (!card.TargetingAvailabilityPredicate?.Invoke(this) ?? false)
					return null;

				Character[] targets;

				switch (card.TargetingType)
				{
					case TargetingType.None:
						return null;
					case TargetingType.All:
						if (allTargets == null)
						{
							if (Opponent.Hero.HasStealth)
							{
								allTargets = new Character[GetFriendlyMinions().Length + GetEnemyMinions().Length + 1];
								allTargets[0] = Hero;
								Array.Copy(GetAllMinions(), 0, allTargets, 1, allMinions.Length);
							}
							else
							{
								allTargets = new Character[GetFriendlyMinions().Length + GetEnemyMinions().Length + 2];
								allTargets[0] = Hero;
								allTargets[1] = Opponent.Hero;
								Array.Copy(GetAllMinions(), 0, allTargets, 2, allMinions.Length);
							}
						}
						targets = allTargets;
						break;
					case TargetingType.FriendlyCharacters:
						if (allFriendly == null)
						{
							allFriendly = new Character[GetFriendlyMinions().Length + 1];
							allFriendly[0] = Hero;
							Array.Copy(friendlyMinions, 0, allFriendly, 1, friendlyMinions.Length);
						}
						targets = allFriendly;
						break;
					case TargetingType.EnemyCharacters:
						if (allEnemies == null)
						{
							if (!Opponent.Hero.HasStealth)
							{
								allEnemies = new Character[GetEnemyMinions().Length + 1];
								allEnemies[0] = Opponent.Hero;
								Array.Copy(enemyMinions, 0, allEnemies, 1, enemyMinions.Length);
							}
							else
								allEnemies = GetEnemyMinions();
						}
						targets = allEnemies;
						break;
					case TargetingType.AllMinions:
						targets = GetAllMinions();
						break;
					case TargetingType.FriendlyMinions:
						targets = GetFriendlyMinions();
						break;
					case TargetingType.EnemyMinions:
						targets = GetEnemyMinions();
						break;
					case TargetingType.Heroes:
						targets = !Opponent.Hero.HasStealth
							? new[] { Hero, Opponent.Hero }
							: new[] { Hero };
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}

				// Filtering for target_if_available
				TargetingPredicate p = card.TargetingPredicate;
				if (p != null)
				{
					if (card.Type == CardType.SPELL || card.Type == CardType.HERO_POWER)
					{
						Character[] buffer = new Character[targets.Length];
						int i = 0;
						for (int j = 0; j < targets.Length; ++j)
						{
							if (!p(targets[j]) || targets[j].CantBeTargetedBySpells) continue;
							buffer[i] = targets[j];
							i++;
						}

						if (i != targets.Length)
						{
							Character[] result = new Character[i];
							Array.Copy(buffer, result, i);
							return result;
						}
						return buffer;
					}
					else
					{
						if (!card.TargetingAvailabilityPredicate?.Invoke(this) ?? false)
							return null;

						Character[] buffer = new Character[targets.Length];
						int i = 0;
						for (int j = 0; j < targets.Length; ++j)
						{
							if (!p(targets[j])) continue;
							buffer[i] = targets[j];
							i++;
						}

						if (i != targets.Length)
						{
							Character[] result = new Character[i];
							Array.Copy(buffer, result, i);
							return result;
						}
						return buffer;
					}
				}
				else if (card.Type == CardType.SPELL || card.Type == CardType.HERO_POWER)
				{
					Character[] buffer = new Character[targets.Length];
					int i = 0;
					for (int j = 0; j < targets.Length; ++j)
					{
						if (targets[j].CantBeTargetedBySpells) continue;
						buffer[i] = targets[j];
						i++;
					}

					if (i != targets.Length)
					{
						Character[] result = new Character[i];
						Array.Copy(buffer, result, i);
						return result;
					}
					return buffer;
				}

				return targets;

				Minion[] GetFriendlyMinions()
				{
					return friendlyMinions ?? (friendlyMinions = BoardZone.GetAll());
				}

				Minion[] GetAllMinions()
				{
					if (allMinions != null)
						return allMinions;

					allMinions = new Minion[GetEnemyMinions().Length + GetFriendlyMinions().Length];
					Array.Copy(enemyMinions, allMinions, enemyMinions.Length);
					Array.Copy(friendlyMinions, 0, allMinions, enemyMinions.Length, friendlyMinions.Length);

					return allMinions;
				}
			}

			void GenerateAttackTargets()
			{
				if (attackTargets != null) return;

				Minion[] eMinions = GetEnemyMinions();
				//var taunts = new Minion[eMinions.Length];
				Minion[] taunts = null;
				int tCount = 0;
				for (int i = 0; i < eMinions.Length; i++)
					if (eMinions[i].HasTaunt)
					{
						if (taunts == null)
							taunts = new Minion[eMinions.Length];
						taunts[tCount] = eMinions[i];
						tCount++;
					}

				if (tCount > 0)
				{
					var targets = new Minion[tCount];
					Array.Copy(taunts, targets, tCount);
					attackTargets = targets;
					isOpHeroValidAttackTarget = false;  // some brawls allow taunt heros and this should be fixed
					return;
				}
				attackTargets = eMinions;

				isOpHeroValidAttackTarget =
					!Opponent.Hero.IsImmune && !Opponent.Hero.HasStealth;
			}

			Minion[] GetEnemyMinions()
			{
				return enemyMinions ?? (enemyMinions = Opponent.BoardZone.GetAll(p => !p.HasStealth && !p.IsImmune));
			}
			#endregion
		}

		/// <summary>
		/// Returns a string which dumps information about this player.
		/// </summary>
		/// <returns></returns>
		public string FullPrint()
		{
			var str = new StringBuilder();
			str.Append($"{Name}[Mana:{RemainingMana}/{OverloadOwed}/{BaseMana}][{OverloadLocked}]");
			//str.Append($"[ENCH {OldEnchants.Count}]");
			//str.Append($"[TRIG {Triggers.Count}]");
			return str.ToString();
		}
	}

	public partial class Controller
	{
		/// <summary>
		/// Maximum amount of cards in the player's hand
		/// </summary>
		public const int MaxHandSize = 10;

		/// <summary>
		/// Maximum amount of mana this player is allowed to spend.
		/// </summary>
		public const int MaxResources = 10;

		/// <summary>
		/// Duration of seconds of this player's turn.
		/// </summary>
		public int TimeOut
		{
			get { return this[GameTag.TIMEOUT]; }
			set { this[GameTag.TIMEOUT] = value; }
		}

		/// <summary>
		/// ID of the player, which is a monotone ranking order starting from 1.
		/// The first player gets PlayerID == 1
		/// </summary>
		public int PlayerId => _playerId;

		/// <summary>
		/// The EntityID of the selected Hero.
		/// </summary>
		public int HeroId
		{
			get { return this[GameTag.HERO_ENTITY]; }
			set { this[GameTag.HERO_ENTITY] = value; }
		}


		/// <summary>
		/// Context in which the controller is performing.
		/// </summary>
		public PlayState PlayState
		{
			get { return (PlayState)this[GameTag.PLAYSTATE]; }
			set { this[GameTag.PLAYSTATE] = (int)value; }
		}

		/// <summary>
		/// Progress this player is making during Mulligan Phase.
		/// <see cref="Mulligan"/>
		/// </summary>
		public Mulligan MulliganState
		{
			get { return (Mulligan)this[GameTag.MULLIGAN_STATE]; }
			set { this[GameTag.MULLIGAN_STATE] = (int)value; }
		}

		/// <summary>
		/// Total amount of mana available to this player.
		/// This value DOES NOT contain temporary mana!
		/// 
		/// This value is limited to 1 turnand should be reset in the next turn.
		/// </summary>
		public int BaseMana
		{
			//get { return this[GameTag.RESOURCES]; }
			//set { this[GameTag.RESOURCES] = value; }
			get { return this[GameTag.RESOURCES]; }
			set { this[GameTag.RESOURCES] = value; }
		}

		/// <summary>
		/// Amount of mana used by this player.
		/// 
		/// This value is limited to 1 turnand should be reset in the next turn.
		/// </summary>
		public int UsedMana
		{
			get
			{
				_data.TryGetValue(GameTag.RESOURCES_USED, out int value);
				return value;
			}
			set { this[GameTag.RESOURCES_USED] = value; }
		}

		/// <summary>
		/// Additionall mana gained during this turn.
		/// </summary>
		public int TemporaryMana
		{
			get
			{
				_data.TryGetValue(GameTag.TEMP_RESOURCES, out int value);
				return value;
			}
			set { this[GameTag.TEMP_RESOURCES] = value; }
		}

		/// <summary>
		/// Indicates if combo power effects of next cards should be executed or not.
		/// 
		/// Combo is active if at least one card has been played this turn.
		/// </summary>
		public bool IsComboActive
		{
			get
			{
				_data.TryGetValue(GameTag.COMBO_ACTIVE, out int value);
				return value == 1;
			}
			set { this[GameTag.COMBO_ACTIVE] =  value ? 1 : 0; }
		}

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

		public int NumCardsDrawnThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_CARDS_DRAWN_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_CARDS_DRAWN_THIS_TURN] = value; }
		}

		public int NumCardsToDraw
		{
			get { return this[GameTag.NUM_CARDS_TO_DRAW]; }
			set { this[GameTag.NUM_CARDS_TO_DRAW] = value; }
		}

		public int NumAttacksThisTurn
		{
			get { return this[GameTag.NUM_ATTACKS_THIS_TURN]; }
			set { this[GameTag.NUM_ATTACKS_THIS_TURN] = value; }
		}

		public int NumCardsPlayedThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_CARDS_PLAYED_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_CARDS_PLAYED_THIS_TURN] = value; }
		}

		public int NumMinionsPlayedThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_MINIONS_PLAYED_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_MINIONS_PLAYED_THIS_TURN] = value; }
		}

		public int NumElementalsPlayedThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_ELEMENTAL_PLAYED_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_ELEMENTAL_PLAYED_THIS_TURN] = value; }
		}

		public int NumElementalsPlayedLastTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_ELEMENTAL_PLAYED_LAST_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_ELEMENTAL_PLAYED_LAST_TURN] = value; }
		}

		public int NumOptionsPlayedThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_OPTIONS_PLAYED_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_OPTIONS_PLAYED_THIS_TURN] = value; }
		}

		public int NumFriendlyMinionsThatAttackedThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_FRIENDLY_MINIONS_THAT_ATTACKED_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_FRIENDLY_MINIONS_THAT_ATTACKED_THIS_TURN] = value; }
		}

		public int NumFriendlyMinionsThatDiedThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_TURN] = value; }
		}

		public int NumFriendlyMinionsThatDiedThisGame
		{
			get { return this[GameTag.NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_GAME]; }
			set { this[GameTag.NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_GAME] = value; }
		}

		public int NumMinionsPlayerKilledThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_MINIONS_PLAYER_KILLED_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.NUM_MINIONS_PLAYER_KILLED_THIS_TURN] = value; }
		}

		public int TotalManaSpentThisGame
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_RESOURCES_SPENT_THIS_GAME, out int value);
				return value;
			}
			set { this[GameTag.NUM_RESOURCES_SPENT_THIS_GAME] = value; }
		}

		public int HeroPowerActivationsThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN, out int value);
				return value;
			}
			set { this[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] = value; }
		}

		public int NumTimesHeroPowerUsedThisGame
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_TIMES_HERO_POWER_USED_THIS_GAME, out int value);
				return value;
			}
			set { this[GameTag.NUM_TIMES_HERO_POWER_USED_THIS_GAME] = value; }
		}

		public int NumSecretsPlayedThisGame
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_SECRETS_PLAYED_THIS_GAME, out int value);
				return value;
			}
			set { this[GameTag.NUM_SECRETS_PLAYED_THIS_GAME] = value; }
		}

		public int NumSpellsPlayedThisGame
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_SPELLS_PLAYED_THIS_GAME, out int value);
				return value;
			}
			set { this[GameTag.NUM_SPELLS_PLAYED_THIS_GAME] = value; }
		}

		public int NumWeaponsPlayedThisGame
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_WEAPONS_PLAYED_THIS_GAME, out int value);
				return value;
			}
			set { this[GameTag.NUM_WEAPONS_PLAYED_THIS_GAME] = value; }
		}

		public int NumMurlocsPlayedThisGame
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_MURLOCS_PLAYED_THIS_GAME, out int value);
				return value;
			}
			set { this[GameTag.NUM_MURLOCS_PLAYED_THIS_GAME] = value; }
		}

		public int NumDiscardedThisGame => DiscardedEntities.Count;

		public int AmountHeroHealedThisTurn
		{
			get
			{
				_data.TryGetValue(GameTag.AMOUNT_HERO_HEALED_THIS_TURN, out int value);
				return value;
			}
			set => this[GameTag.AMOUNT_HERO_HEALED_THIS_TURN] = value;
		}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member


		/// <summary>
		/// Amount of turns left for this player.
		/// 
		/// This is a special tag used for the spell Time Warp, which grants 
		/// the caster an additional turn.
		/// </summary>
		public int NumTurnsLeft
		{
			get
			{
				_data.TryGetValue(GameTag.NUM_TURNS_LEFT, out int value);
				return value;
			}
			set { this[GameTag.NUM_TURNS_LEFT] = value; }
		}

		/// <summary>
		/// Amount of mana crystals which will be locked during the next turn.
		/// </summary>
		public int OverloadOwed
		{
			get
			{
				_data.TryGetValue(GameTag.OVERLOAD_OWED, out int value);
				return value;
			}
			set { this[GameTag.OVERLOAD_OWED] = value; }
		}

		/// <summary>
		/// Amount of mana crystals locked this turn.
		/// 
		/// The subtraction of BASE_MANA and this value gives the available
		/// resources during this turn.
		/// </summary>
		public int OverloadLocked
		{
			get
			{
				_data.TryGetValue(GameTag.OVERLOAD_LOCKED, out int value);
				return value;
			}
			set { this[GameTag.OVERLOAD_LOCKED] = value; }
		}

		/// <summary>
		/// Sum of locked mana crystals during the entire game.
		/// </summary>
		public int OverloadThisGame
		{
			get
			{
				_data.TryGetValue(GameTag.OVERLOAD_THIS_GAME, out int value);
				return value;
			}
			set { this[GameTag.OVERLOAD_THIS_GAME] = value; }
		}

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

		public int LastCardPlayed
		{
			get { return this[GameTag.LAST_CARD_PLAYED]; }
			set { this[GameTag.LAST_CARD_PLAYED] = value; }
		}

		public int LastCardDrawn
		{
			get { return this[GameTag.LAST_CARD_DRAWN]; }
			set { this[GameTag.LAST_CARD_DRAWN] = value; }
		}

		public int LastCardDiscarded
		{
			get { return this[GameTag.LAST_CARD_DISCARDED]; }
			set { this[GameTag.LAST_CARD_DISCARDED] = value; }
		}

		public bool SeenCthun
		{
			get
			{
				_data.TryGetValue(GameTag.SEEN_CTHUN, out int value);
				return value == 1;
			}
			set { this[GameTag.SEEN_CTHUN] = value ? 1 : 0; }
		}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		/// <summary>
		/// The entity which is a copy of the real C'Thun entity in deck.
		/// This proxy is used to display and store all buffs from rituals.
		/// The real C'Thun will mirror the proxy C'Thun.
		/// </summary>
		public int ProxyCthun
		{
			get
			{
				_data.TryGetValue(GameTag.PROXY_CTHUN, out int value);
				return value;
			}
			set { this[GameTag.PROXY_CTHUN] = value; }
		}

		/// <summary>
		/// Returns true if for this player all cards and powers that restore Health deal damage instead.
		/// (e.g. True when Auchenai Soulpriest is in play.)
		/// </summary>
		public bool RestoreToDamage
		{
			get => ControllerAuraEffects[GameTag.HEALING_DOES_DAMAGE] > 0;
			set => ControllerAuraEffects[GameTag.HEALING_DOES_DAMAGE] = value ? 1 : 0;
		}

		/// <summary>
		/// Returns true if for this player all battlecries should be executed another time.
		/// This is applicable when Brann BronzeBeard is in play.
		/// </summary>
		public bool ExtraBattlecry
		{
			get => ControllerAuraEffects[GameTag.EXTRA_BATTLECRIES_BASE] > 0 ||
			       ControllerAuraEffects[GameTag.EXTRA_MINION_BATTLECRIES_BASE] == 1;
			set => ControllerAuraEffects[GameTag.EXTRA_BATTLECRIES_BASE] = value ? 1 : 0;
		}

		/// <summary>
		/// Returns true if for this player all end turn effects should be executed another time.
		/// This is applicable when Drakkari Enchanter is in play.
		/// </summary>
		public bool ExtraEndTurnEffect
		{
			get => ControllerAuraEffects[GameTag.EXTRA_END_TURN_EFFECT] > 0;
			set => ControllerAuraEffects[GameTag.EXTRA_END_TURN_EFFECT] = value ? 1 : 0;
		}

		/// <summary>
		/// Returns true if for this player hero power is disabled.
		/// </summary>
		public bool HeroPowerDisabled
		{
			get => ControllerAuraEffects[GameTag.HERO_POWER_DISABLED] == 1;
			set => ControllerAuraEffects[GameTag.HERO_POWER_DISABLED] = value ? 1 : 0;
		}

		/// <summary>
		/// Returns true if this player automatically gets both options instead of having to
		/// choose one.
		/// This is applicable when Fandral Staghelm is in play.
		/// </summary>
		public bool ChooseBoth
		{
			get => ControllerAuraEffects[GameTag.CHOOSE_BOTH] == 1;
			set => ControllerAuraEffects[GameTag.CHOOSE_BOTH] = value ? 1 : 0;
		}

		/// <summary>
		/// Amount of current Spell Damage bonus for this Controller.
		/// </summary>
		public int CurrentSpellPower
		{
			get => BoardZone.Sum(m => m.SpellPower)
				+ (Hero.NativeTags.ContainsKey(GameTag.SPELLPOWER) ? Hero.NativeTags[GameTag.SPELLPOWER] : 0)
				+ ControllerAuraEffects[GameTag.SPELLPOWER];
		}

		public int AmountHealedThisGame
		{
			get => this[GameTag.AMOUNT_HEALED_THIS_GAME];
			set => this[GameTag.AMOUNT_HEALED_THIS_GAME] = value;
		}

		public int NumHeroPowerDamageThisGame
		{
			get => this[GameTag.NUM_HERO_POWER_DAMAGE_THIS_GAME];
			set => this[GameTag.NUM_HERO_POWER_DAMAGE_THIS_GAME] = value;
		}

		private int _currentSpellPower;
		private Controller _opponent;
	}
}
