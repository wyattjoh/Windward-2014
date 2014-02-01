/*
 * ----------------------------------------------------------------------------
 * "THE BEER-WARE LICENSE" 
 * As long as you retain this notice you can do whatever you want with this 
 * stuff. If you meet an employee from Windward meet some day, and you think
 * this stuff is worth it, you can buy them a beer in return. Windward Studios
 * ----------------------------------------------------------------------------
 */


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Deployment.Application;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using log4net;
using Windwardopolis2.game_ai;
using Windwardopolis2Library;
using Windwardopolis2Library.ai_interface;
using Windwardopolis2Library.map;
using Windwardopolis2Library.units;

namespace Windwardopolis2.game_engine
{
	public class Engine
	{


		private readonly Framework framework;

		internal ConcurrentQueue<AiMessage> RemoteMessages;

		private int numTicksToPrepareMove;

		internal class AiMessage
		{
			/// <summary>
			/// The Player this message is for.
			/// </summary>
			public Player Player { get; private set; }

			protected AiMessage(Player player)
			{
				Player = player;
			}
		}

		internal class AiPathMessage : AiMessage
		{

			/// <summary>
			/// The new path for this player. If 0 length do not update the path.
			/// </summary>
			public List<Point> Path { get; private set; }

			/// <summary>
			/// The new pickup list for this player. If 0 length do not update the pickup list.
			/// </summary>
			public List<Passenger> Pickup { get; private set; }

			/// <summary>
			/// Initializes a new instance of the <see cref="T:System.Object" /> class.
			/// </summary>
			public AiPathMessage(Player player, List<Point> path, List<Passenger> pickup) : base(player)
			{
				Path = path;
				Pickup = pickup;
			}
		}

		internal class AiPowerupMessage : AiMessage
		{
			public PlayerAIBase.CARD_ACTION Action { get; private set; }
			public PowerUp Card { get; private set; }

			public AiPowerupMessage(Player player, PlayerAIBase.CARD_ACTION action, PowerUp card) : base(player)
			{
				Action = action;
				Card = card;
			}
		}

		internal List<Company> companies;

		/// <summary>
		///     All of the passengers
		/// </summary>
		internal List<Passenger> Passengers;

		internal List<CoffeeStore> stores; 

		public delegate void PlayerOrdersEvent(Player player, List<Point> path, List<Passenger> pickUp);

		// The game map.
		internal GameMap MainMap { get; private set; }

		/// <summary>
		///     All of the players.
		/// </summary>
		internal List<Player> Players { get; private set; }

		/// <summary>
		///     Which game we are playing (zero based).
		/// </summary>
		public int GameOn { get; set; }


		// anytime we need a random number.
		private static readonly Random rand = new Random();

		private static readonly ILog log = LogManager.GetLogger(typeof (Engine));

		public Engine(Framework framework, string mapFilename)
		{
			GameOn = 0;
			RemoteMessages = new ConcurrentQueue<AiMessage>();
			Players = new List<Player>();
			numTicksToPrepareMove = 0;

			this.framework = framework;

			// create the map
			MainMap = GameMap.OpenMap(mapFilename, companies);
			cacheStartLocations = null;
		}

		#region initialize

		private List<BoardLocation> cacheStartLocations;

		internal List<BoardLocation> StartLocations
		{
			get
			{
				if (cacheStartLocations != null)
					return cacheStartLocations;
				cacheStartLocations = new List<BoardLocation>();
				for (int x = 0; x < MainMap.Width; x++)
					for (int y = 0; y < MainMap.Height; y++)
					{
						MapSquare square = MainMap.Squares[x][y];
						// the point is the center of the car. Put it in the middle of the tile and the middle of correct lane in that tile.
						const int addMiddleNearLane = TileMovement.UNITS_PER_TILE/4;
						const int addMiddleTile = TileMovement.UNITS_PER_TILE/2;
						const int addMiddleFarLane = (3*TileMovement.UNITS_PER_TILE)/4;
						switch (square.StartPosition)
						{
							case MapSquare.COMPASS_DIRECTION.NORTH:
								cacheStartLocations.Add(new BoardLocation(new Point(x*TileMovement.UNITS_PER_TILE + addMiddleFarLane,
									y*TileMovement.UNITS_PER_TILE + addMiddleTile),
									TileMovement.GetMove(MapSquare.COMPASS_DIRECTION.NORTH, MapSquare.COMPASS_DIRECTION.NORTH)));
								break;
							case MapSquare.COMPASS_DIRECTION.EAST:
								cacheStartLocations.Add(new BoardLocation(new Point(x*TileMovement.UNITS_PER_TILE + addMiddleTile,
									y*TileMovement.UNITS_PER_TILE + addMiddleFarLane),
									TileMovement.GetMove(MapSquare.COMPASS_DIRECTION.EAST, MapSquare.COMPASS_DIRECTION.EAST)));
								break;
							case MapSquare.COMPASS_DIRECTION.SOUTH:
								cacheStartLocations.Add(new BoardLocation(new Point(x*TileMovement.UNITS_PER_TILE + addMiddleNearLane,
									y*TileMovement.UNITS_PER_TILE + addMiddleTile),
									TileMovement.GetMove(MapSquare.COMPASS_DIRECTION.SOUTH, MapSquare.COMPASS_DIRECTION.SOUTH)));
								break;
							case MapSquare.COMPASS_DIRECTION.WEST:
								cacheStartLocations.Add(new BoardLocation(new Point(x*TileMovement.UNITS_PER_TILE + addMiddleTile,
									(y*TileMovement.UNITS_PER_TILE + addMiddleNearLane)),
									TileMovement.GetMove(MapSquare.COMPASS_DIRECTION.WEST, MapSquare.COMPASS_DIRECTION.WEST)));
								break;
						}
					}

				cacheStartLocations.Sort((loc1, loc2) => rand.Next());
				return cacheStartLocations;
			}
		}

		internal List<Point> BusStopLocations
		{
			get
			{
				List<Point> locations = new List<Point>();
				for (int x = 0; x < MainMap.Width; x++)
					for (int y = 0; y < MainMap.Height; y++)
					{
						MapSquare square = MainMap.Squares[x][y];
						if (square.Tile.Type == MapTile.TYPE.BUS_STOP)
							locations.Add(new Point(x, y));
					}

				locations.Sort((loc1, loc2) => rand.Next());
				return locations;
			}
		}

		internal List<Point> CoffeeStopLocations
		{
			get
			{
				List<Point> locations = new List<Point>();
				for (int x = 0; x < MainMap.Width; x++)
					for (int y = 0; y < MainMap.Height; y++)
					{
						MapSquare square = MainMap.Squares[x][y];
						if (square.Tile.Type == MapTile.TYPE.COFFEE_STOP)
							locations.Add(new Point(x, y));
					}

				locations.Sort((loc1, loc2) => rand.Next());
				return locations;
			}
		}

		public void Initialize()
		{
			numTicksToPrepareMove = 0;

			// new map
			if (GameOn != 0)
			{
				MainMap = GameMap.OpenMap(framework.MapFilename, companies);
				framework.mainWindow.NewMap();
				cacheStartLocations = null;
			}

			// set player objects to start of new run
			foreach (Player plyr in Players)
				plyr.Reset();

			// place cars at start locations - randomize which order they are placed.
			List<BoardLocation> starts = StartLocations.OrderBy(sl => rand.Next()).ToList();
			for (int index = 0; index < Players.Count; index++)
				Players[index].Limo.Location = new BoardLocation(starts[index]);

			// set up companies
			Company.GenerateCompaniesAndPassengers(out companies, out Passengers);
			List<Point> busStops = BusStopLocations.OrderBy(sl => rand.Next()).ToList();
			for (int index = companies.Count; index < busStops.Count; index++)
				MainMap.Squares[busStops[index].X][busStops[index].Y].Company = null;
			for (int index = 0; index < companies.Count; index++)
			{
				companies[index].BusStop = busStops[index];
				MainMap.Squares[busStops[index].X][busStops[index].Y].Company = companies[index];
			}

			// set up coffee stores
			CoffeeStore.GenerateStores(CoffeeStopLocations, out stores);

			// Assign the powerups to each player
			foreach (Player plyr in Players)
			{
				plyr.PowerUpInAction = null;
				plyr.PowerUpNextBusStop = null;
				plyr.PowerUpsDeck.Clear();
				plyr.PowerUpsDrawn.Clear();

				// they get a random 1/3 of the 1.2X powerups
				List<PowerUp> multPowerUps = PowerUp.atOneStore(Passengers, companies);
				multPowerUps = multPowerUps.OrderBy(pu => rand.Next()).Take(multPowerUps.Count/3).ToList();
				plyr.PowerUpsDeck.AddRange(multPowerUps);
				plyr.PowerUpsDeck.AddRange(PowerUp.atAllStores());
			}

			// ask each player for their initial orders.
			foreach (Player plyr in Players)
				plyr.Setup(MainMap, plyr, Players, companies, stores, Passengers, PlayerAddOrder, PlayerPowerupsEvent);

			framework.mainWindow.TurnNumber(framework.GameEngine.GameOn + 1);
		}

		public void RestartPlayer(Player player)
		{
			player.Setup(MainMap, player, Players, companies, stores, Passengers, PlayerAddOrder, PlayerPowerupsEvent);
		}

		#endregion

		/// <summary>
		///     All the limos.
		/// </summary>
		internal List<Limo> Limos
		{
			get { return Players.Select(pl => pl.Limo).ToList(); }
		}

		#region execute turn/phase

		public void Tick()
		{
			ValidateData();

			// handle any messages we received
			ProcessAllMoveMessages();

			// handle all Player.PowerUpInAction power-ups
			foreach (Player plyr in Players)
			{
				if (plyr.PowerUpInAction == null)
					continue;
				ProcessPowerup(plyr);
				plyr.PowerUpInAction = null;
			}

			// undo slow downs, icon displays
			foreach (Player plyr in Players)
			{
				plyr.Tick();

				if (plyr.TicksToFullSpeed <= 0) 
					continue;
				plyr.TicksToFullSpeed --;
				if (plyr.TicksToFullSpeed != 0) 
					continue;
				// don't undo if transiting at 1/4 speed
				if (plyr.PowerUpThisTransit != null && plyr.PowerUpThisTransit.Card == PowerUp.CARD.MULT_DELIVERY_QUARTER_SPEED)
					continue;
				plyr.Limo.IsQuarterSpeed = false;
				InfoAndStatus(string.Format("returning player {0} to full speed", plyr.Name));
			}

			// repair flat tires
			foreach (Player plyr in Players.Where(plyr => plyr.Limo.TickCountToFixFlat > 0))
			{
				plyr.Limo.TickCountToFixFlat--;
				if (plyr.Limo.TickCountToFixFlat == 0)
				{
					plyr.Limo.IsFlatTire = false;
					InfoAndStatus(string.Format("{0} has their flat tire fixed", plyr.Name));
				}
			}

			if (numTicksToPrepareMove-- <= 0)
			{
				foreach (Player plyr in Players)
					try
					{
						PrepareToMove(plyr);
					}
					catch (Exception ex)
					{
						Trap.trap();
						log.Error(string.Format("{0} : PrepareToMove", plyr.Name), ex);
						plyr.Limo.PathTiles.Clear();
					}
				numTicksToPrepareMove = 6;
			}

			// accelerate (once per turn, not once per step)
			foreach (Player plyr in Players)
			{
				plyr.Limo.Accelerate();
				plyr.Limo.AccruedSteps += plyr.Limo.Speed/TileMovement.MAX_STRAIGHT_SPEED;
				plyr.Limo.StepsRemaining = (int) plyr.Limo.AccruedSteps;
				plyr.Limo.AccruedSteps -= plyr.Limo.StepsRemaining;
			}

			// we run till none can move (the for is because I'm paranoid about somehow the StepsRemaining is reset).
			List<Point> signalsSet = new List<Point>();
			int prevStepsRemainingTotal = int.MaxValue;
			for (int iter = Players.Count; iter >= 0; iter--)
			{
				int totalStepsRemaining = Players.Sum(plyr => plyr.Limo.StepsRemaining);
				if (totalStepsRemaining == 0)
					break;
				bool lastPass = (iter == 0) || (totalStepsRemaining == prevStepsRemainingTotal);
				prevStepsRemainingTotal = totalStepsRemaining;
				foreach (Player plyr in Players)
					try
					{
						MoveLimo(plyr, signalsSet, lastPass);
					}
					catch (Exception ex)
					{
						log.Error(string.Format("{0} : MoveLimo", plyr.Name), ex);
						plyr.Limo.PathTiles.Clear();
					}

				if (lastPass)
					break;
			}

			ValidateData();
		}

		private void ProcessPowerup(Player plyr)
		{

			try
			{
				switch (plyr.PowerUpInAction.Card)
				{
					case PowerUp.CARD.ALL_OTHER_CARS_QUARTER_SPEED:
						foreach (Player plyrOn in Players.Where(plyrOn => plyrOn != plyr))
						{
							plyrOn.Limo.IsQuarterSpeed = true;
							plyrOn.TicksToFullSpeed += Framework.TICKS_PER_SECOND * 30;
						}

						InfoAndStatus(string.Format("Reducing all cars except {0} to 1/4 speed", plyr.Name));
						foreach (Player plyrOn in Players)
							plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpInAction);
						break;

					case PowerUp.CARD.CHANGE_DESTINATION:
						// need to have a player, player must have a passenger
						if (plyr.PowerUpInAction.Player == null)
						{
							Trap.trap();
							if (log.IsWarnEnabled)
								log.Warn(string.Format("ERROR card CHANGE_DESTINATION - has no player"));
							plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpInAction);
							break;
						}
						Passenger psngr = plyr.PowerUpInAction.Player.Passenger;
						if (psngr == null)
						{
							if (log.IsWarnEnabled)
								log.Warn(string.Format("ERROR card CHANGE_DESTINATION - {0} has no passenger",
									plyr.PowerUpInAction.Player.Name));
							plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpInAction);
							break;
						}

						// not allowed if within 3 squares of destination
						Point ptCar = BoardLocation.MapToTile(plyr.PowerUpInAction.Player.Limo.Front);
						Point ptDest = plyr.PowerUpInAction.Player.Passenger.Destination.BusStop;
						if (Math.Abs(ptCar.X - ptDest.X) + Math.Abs(ptCar.Y - ptDest.Y) <= 3)
						{
							plyr.PowerUpsDrawn.Add(plyr.PowerUpInAction);
							if (log.IsWarnEnabled)
								log.Warn(string.Format("ERROR card CHANGE_DESTINATION - {0} is within 3 squares of destination",
									plyr.PowerUpInAction.Player.Passenger.Name));
							plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpInAction);
							break;
						}

						// far enough away, move them.
						Company newCompanyDest = companies.Where(cpy => cpy != psngr.Destination).OrderBy(pu => rand.Next()).First();
						// put old destination back into route, remove new one from route
						psngr.Route.Add(psngr.Destination);
						psngr.Route.Remove(newCompanyDest);
						psngr.Destination = newCompanyDest;

						plyr.PowerUpInAction.Player.AddToDisplay(plyr.PowerUpInAction, Framework.TICKS_PER_SECOND * 5);

						InfoAndStatus(string.Format("Change destination of {0} to {1}", psngr.Name, newCompanyDest.Name));
						foreach (Player plyrOn in Players)
							plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpInAction);
						break;

					case PowerUp.CARD.MOVE_PASSENGER:
						// must have passenger, can't be in car
						if (plyr.PowerUpInAction.Passenger == null)
						{
							Trap.trap();
							if (log.IsWarnEnabled)
								log.Warn(string.Format("ERROR card MOVE_PASSENGER - no passenger"));
							plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpInAction);
							break;
						}
						if (plyr.PowerUpInAction.Passenger.Car != null)
						{
							if (log.IsWarnEnabled)
								log.Warn(string.Format("ERROR card MOVE_PASSENGER {0} is in a car", plyr.PowerUpInAction.Passenger));
							plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpInAction);
							break;
						}

						// move them to any other company except their destination.
						Company dest = MovePassengerToRandomCompany(plyr.PowerUpInAction.Passenger);

						AddToAllPlayerDisplays(plyr.PowerUpInAction);

						InfoAndStatus(string.Format("Move {0} to {1}", plyr.PowerUpInAction.Passenger.Name, dest.Name));
						foreach (Player plyrOn in Players)
							plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpInAction);
						break;

					case PowerUp.CARD.RELOCATE_ALL_CARS:
						List<Point> taken = new List<Point>();
						foreach (Player plyrOn in Players)
						{
							// get the best point to where the car was headed
							Point ptLimoDest = plyrOn.Limo.PathTiles.Count > 0 ? plyrOn.Limo.PathTiles.Last()
												: (plyrOn.Passenger != null ? plyrOn.Passenger.Destination.BusStop : Point.Empty);

							Point ptMap = new Point();
							MapTile tile;
							while (true)
							{
								ptMap.X = rand.Next(MainMap.Width);
								ptMap.Y = rand.Next(MainMap.Height);

								// at least 2 away from where it's headed
								if (ptLimoDest != Point.Empty)
									if (Math.Abs(ptMap.X - ptLimoDest.X) + Math.Abs(ptMap.Y - ptLimoDest.Y) < 4)
										continue;

								tile = MainMap.Squares[ptMap.X][ptMap.Y].Tile;
								if ((! taken.Contains(ptMap)) && tile.IsDriveable &&
								    (tile.Direction == MapTile.DIRECTION.EAST_WEST || tile.Direction == MapTile.DIRECTION.NORTH_SOUTH))
									break;
							}
							taken.Add(ptMap);


							List<Point> path = null;
							MapSquare.COMPASS_DIRECTION direction = MapSquare.COMPASS_DIRECTION.NONE;
							if (ptLimoDest != Point.Empty)
							{
								path = SimpleAStar.CalculatePath(MainMap, ptMap, ptLimoDest);
								if (path.Count > 1)
									direction = MapSquare.PointsDirection(path[0], path[1]);
								else Trap.trap();
							}
							if (direction == MapSquare.COMPASS_DIRECTION.NONE)
							{
								if (tile.Direction == MapTile.DIRECTION.NORTH_SOUTH)
									direction = rand.Next(2) == 0 ? MapSquare.COMPASS_DIRECTION.NORTH : MapSquare.COMPASS_DIRECTION.SOUTH;
								else
									direction = rand.Next(2) == 0 ? MapSquare.COMPASS_DIRECTION.WEST : MapSquare.COMPASS_DIRECTION.EAST;
							}

							if (log.IsDebugEnabled)
							{
								log.Debug(string.Format("relocate {0} to {1} with destination {2} in direction {3}", 
													plyrOn.Name, ptMap, ptLimoDest, direction));
								if (path != null && path.Count > 0)
									log.Debug(string.Format("     path: {0}", string.Join(", ", path)));
							}

							// reset the limo to this new location.
							plyrOn.Limo.Reset();
							plyrOn.Limo.SetTileLocation(MainMap.Squares[ptMap.X][ptMap.Y], ptMap, direction);
							if (path != null)
								plyrOn.Limo.PathTiles.AddRange(path);

							if (log.IsDebugEnabled && plyrOn.Limo.PathTiles.Count > 0)
								log.Debug(string.Format("     Location: {0}, path: {1} -> {2}", plyrOn.Limo.Location, plyrOn.Limo.PathTiles[0], plyrOn.Limo.PathTiles[plyrOn.Limo.PathTiles.Count - 1]));
						}

						AddToAllPlayerDisplays(plyr.PowerUpInAction);

						InfoAndStatus("moved all limos to random locations");
						foreach (Player plyrOn in Players)
							plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpInAction);
						break;

					case PowerUp.CARD.RELOCATE_ALL_PASSENGERS:
						// passengers must be in a company, not in a car. AND if they are an enemy of the passenger the player has
						// and are at the player's passenger's destination - they are not moved.
						List<Passenger> enemies = new List<Passenger>();
						if (plyr.Passenger != null)
							enemies.AddRange(plyr.Passenger.Enemies.Where(psngrOn => psngrOn.Lobby == plyr.Passenger.Destination));
						foreach (Passenger passenger in Passengers.Where(ps => ps.Car == null && (! enemies.Contains(ps))))
							MovePassengerToRandomCompany(passenger);

						AddToAllPlayerDisplays(plyr.PowerUpInAction);

						foreach (Player plyrOn in Players)
							plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpInAction);
						break;

					case PowerUp.CARD.STOP_CAR:
						// must be in a bus stop
						if (plyr.PowerUpInAction.Player == null)
						{
							Trap.trap();
							if (log.IsWarnEnabled)
								log.Warn(string.Format("ERROR card STOP_CAR - no player"));
							plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpInAction);
							break;
						}

						// not allowed if within 3 squares of destination
						Point ptCarFront = BoardLocation.MapToTile(plyr.PowerUpInAction.Player.Limo.Front);
						if (plyr.PowerUpInAction.Player.Passenger != null)
						{
							Point ptCarDest = plyr.PowerUpInAction.Player.Passenger.Destination.BusStop;
							if (Math.Abs(ptCarFront.X - ptCarDest.X) + Math.Abs(ptCarFront.Y - ptCarDest.Y) <= 3)
							{
								plyr.PowerUpsDrawn.Add(plyr.PowerUpInAction);
								if (log.IsWarnEnabled)
									log.Warn(string.Format("ERROR card STOP_CAR - {0} is within 3 squares of destination",
										plyr.PowerUpInAction.Player.Passenger.Name));
								plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpInAction);
								break;
							}
						}

						// not allowed if in a bus stop
						MapTile tileFront = MainMap.Squares[ptCarFront.X][ptCarFront.Y].Tile;
						Point ptCarBack = BoardLocation.MapToTile(plyr.PowerUpInAction.Player.Limo.Back);
						MapTile tileBack = MainMap.Squares[ptCarBack.X][ptCarBack.Y].Tile;
						if (tileFront.IsStop || tileBack.IsStop)
						{
							plyr.PowerUpsDrawn.Add(plyr.PowerUpInAction);
							if (log.IsWarnEnabled)
								log.Warn(string.Format("ERROR card STOP_CAR - {0} is within a bus/coffee stop",
									plyr.PowerUpInAction.Player.Name));
							plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpInAction);
							break;
						}

						plyr.PowerUpInAction.Player.Limo.IsFlatTire = true;
						plyr.PowerUpInAction.Player.Limo.TickCountToFixFlat = 60 * 30;

						InfoAndStatus(string.Format("{0} has a flat tire - stopped", plyr.PowerUpInAction.Player.Name));
						foreach (Player plyrOn in Players)
							plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpInAction);
						break;
				}
			}
			finally
			{
				plyr.PowerUpInAction = null;
				ValidateData();
			}
		}

		private void AddToAllPlayerDisplays(PowerUp pu)
		{
			foreach (Player plyrOn in Players)
				plyrOn.AddToDisplay(pu, Framework.TICKS_PER_SECOND*5);
		}

		private Company MovePassengerToRandomCompany(Passenger psngr)
		{
			Company src = psngr.Lobby;
			Company dest = companies.Where(
				cpy => cpy != src && cpy != psngr.Destination)
				.OrderBy(pu => rand.Next()).First();
			psngr.Move(dest);
			src.Passengers.Remove(psngr);
			dest.Passengers.Add(psngr);

			InfoAndStatus(string.Format("{0} moved from {1} to {2}", psngr.Name, src.Name, dest.Name));
			return dest;
		}

		public void ProcessAllMoveMessages()
		{
			while (true)
			{
				AiMessage msg;
				if (!RemoteMessages.TryDequeue(out msg))
					return;
				AiPathMessage pathMsg = msg as AiPathMessage;
				if (pathMsg != null)
				{
					PlayerOrders(msg.Player, pathMsg.Path, pathMsg.Pickup);
					numTicksToPrepareMove = 0;
					continue;
				}
				AiPowerupMessage puMsg = msg as AiPowerupMessage;
				if (puMsg == null)
				{
					Trap.trap();
					continue;
				}
				// handle power-up
				PlayerPowerups(puMsg.Player, puMsg.Action, puMsg.Card);
			}
		}

		/// <summary>
		///     Calculates the estimated path for each limo. Resets count of steps taken. Adjusts speed.
		/// </summary>
		/// <param name="plyr">The player being reset for the move.</param>
		private void PrepareToMove(Player plyr)
		{
			if (log.IsDebugEnabled)
				log.Debug(string.Format("{0} : PrepareToMove, location = {1}", plyr.Name, plyr.Limo.Location));

			plyr.Limo.Future.Clear();
			Point ptOn = plyr.Limo.Location.MapPosition;
			Point ptMapStart = BoardLocation.MapToTile(ptOn);
			plyr.Limo.AddFuture(ptOn);
			BoardLocation location = new BoardLocation(plyr.Limo.Location);
			int pathOffset = 0;

			for (int ind = 0; ind < Limo.NUM_FUTURE; ind++)
			{
				if (location.IsMoveInsideTile)
				{
					location.Move();
					plyr.Limo.AddFuture(location.MapPosition);
					continue;
				}

				// get where we enter the next tile
				Point ptNextMap = location.NextPosition;
				Point ptNextTile = location.NextTilePosition;
				MapSquare squareNext = MainMap.Squares[ptNextTile.X][ptNextTile.Y];
				MapSquare.COMPASS_DIRECTION dirOn = location.Direction;

				// if we enter a stop or signal, we're done guessing the future. We've actually moved the car half way into the
				// intersection in this case. But it's more efficient than getting the front location each step and
				// we need the path up to the tile edge. 
				// We go on a signal regardless of color because it can be green when we get there and this is used to see if cars 
				// the other way can turn left.
				if (ptNextTile != ptMapStart)
				{
					if (squareNext.IsStopAllGreen(dirOn))
					{
						if (log.IsDebugEnabled)
							log.Debug(string.Format("{0} : PrepareToMove hit stop at = {1}, {2}", plyr.Name, ptNextTile, dirOn));
						break;
					}
					// might be a u-turn
					ptMapStart = Point.Empty;
				}

				// move into the next tile - so below calculations are based on the tile we've just entered (but can now move through in any direction).
				location.Move();
				plyr.Limo.AddFuture(location.MapPosition);

				// we now work with the next position because that moves us into the next tile if we are at offset 0 and going north or west.
				// this has to be the next map position converted to tile position because that is sometimes this tile position, sometimes next.
				Trap.trap(location.MapPosition != ptNextMap);
				ptNextMap = location.NextPosition;
				ptNextTile = BoardLocation.MapToTile(ptNextMap);

				// we increment it here because a U-turn does not move to the next tile and we need the same offset again.
				if ((pathOffset < plyr.Limo.PathTiles.Count) && (plyr.Limo.PathTiles[pathOffset] == ptNextTile))
					pathOffset++;

				// get our new destination - from the path if valid
				MapSquare.COMPASS_DIRECTION destDirection = MapSquare.COMPASS_DIRECTION.NONE;
				if (plyr.Limo.PathTiles.Count > pathOffset)
				{
					int x = plyr.Limo.PathTiles[pathOffset].X - ptNextTile.X;
					int y = plyr.Limo.PathTiles[pathOffset].Y - ptNextTile.Y;
					if (Math.Abs(x) + Math.Abs(y) == 1)
						destDirection = MapSquare.PointsDirection(ptNextTile, plyr.Limo.PathTiles[pathOffset]);
					else if (log.IsWarnEnabled)
						log.Warn(string.Format("{0} : PrepareToMove path error at = {1}, {2}; pathOff={3}, count={4}; x={5}, y={6}",
							plyr.Name, ptNextTile, dirOn, pathOffset, plyr.Limo.PathTiles.Count, x, y));
				}

				// if no path, we get the next one only if just one choice (straight/curves) or straight is allowed.
				// in other words, entering the top of a T we assume straight across. Entering from the base we stop (returns NONE).
				if (destDirection == MapSquare.COMPASS_DIRECTION.NONE)
				{
					destDirection = squareNext.Tile.GetStraightNext(dirOn);
					if (log.IsWarnEnabled)
						log.Warn(string.Format("{0} : PrepareToMove no dirNext at = {1}, {2}; pathOff={3}, count={4}", plyr.Name,
							ptNextTile, dirOn, pathOffset, plyr.Limo.PathTiles.Count));
				}
				if (destDirection == MapSquare.COMPASS_DIRECTION.NONE)
					break;

				// get the inter-tile movement and assign
				TileMovement movement = TileMovement.GetMove(dirOn, destDirection);
				location.SetMapPosition(location.MapPosition, movement);
				if (log.IsDebugEnabled)
					log.Debug(string.Format("{0} : PrepareToMove next tile complete, location = {1}", plyr.Name, location));
			}

			if (log.IsDebugEnabled)
			{
				if (plyr.Limo.Future.Count == 0)
					log.Debug(string.Format("{0} : PrepareToMove complete, future, len=0", plyr.Name));
				else
					log.Debug(string.Format("{0} : PrepareToMove complete, future = {1}..{2}, len={3}", plyr.Name,
						plyr.Limo.Future[0], plyr.Limo.Future[plyr.Limo.Future.Count - 1], plyr.Limo.Future.Count));
			}
		}

		/// <summary>
		/// Move the limo.
		/// </summary>
		/// <param name="plyr">The player who's limo is being moved.</param>
		/// <param name="signalsSet">Tiles where the signal has had it's value set/locked.</param>
		/// <param name="lastPass">true if last pass. Any slow down or stopping occurs this time.</param>
		private void MoveLimo(Player plyr, List<Point> signalsSet, bool lastPass)
		{
			bool isDebugEnabled = log.IsDebugEnabled; // 4.7% of total CPU time when called in each case here.
			if (isDebugEnabled)
				log.Debug(string.Format("{0} : MoveLimo, speed = {1}, accrued = {2}, steps = {3}, location = {4}", plyr.Name,
					plyr.Limo.Speed, plyr.Limo.AccruedSteps, plyr.Limo.StepsRemaining, plyr.Limo.Location));

			// if stopped, we do no movement.
			if (plyr.Limo.IsFlatTire)
				return;

			// if stuck, un-stick it
			if (plyr.Limo.NumTurnsStopped >= Limo.MAX_TURNS_STOPPED)
			{
				plyr.Limo.Accelerate();
				plyr.Limo.IsForceMove = true;
				plyr.Limo.StepsRemaining = TileMovement.MAX_STRAIGHT_SPEED;
			}
			else
				plyr.Limo.IsForceMove = false;

			// once for each step on this tick
			while (plyr.Limo.StepsRemaining > 0)
			{
				// there's the point that the center of the sprite is on - actual movement goes from that
				// plyr.Limo.Location is map & tile on.
				Point ptCenterNextMapPos = plyr.Limo.Location.NextPosition;

				// there's the point the front of the sprite is at - stop signs and traffic go from that.
				Point ptFrontMapPos = plyr.Limo.Front;
				Point ptFrontTile = BoardLocation.MapToTile(ptFrontMapPos);
				Point ptFrontNextMapPos = plyr.Limo.FrontNextStep;
				Point ptFrontNextTile = BoardLocation.MapToTile(ptFrontNextMapPos);
				if (isDebugEnabled)
					log.Debug(string.Format("{0} : MoveLimo, ptFront = {1} ({2}), ptNext = {3} ({4})", plyr.Name, ptFrontMapPos,
						ptFrontTile, ptFrontNextMapPos, ptFrontNextTile));

				MapSquare square = MainMap.Squares[ptFrontNextTile.X][ptFrontNextTile.Y];
				MapSquare.COMPASS_DIRECTION direction = plyr.Limo.Location.Direction;

				// stop sign or red light - we stop. Only test when we would enter the tile (once in, you don't stop!)
				if ((! plyr.Limo.IsForceMove) && (ptFrontTile != ptFrontNextTile))
				{
					bool isStop = square.IsStop(direction);
					int limoHalfLength = plyr.Limo.VehicleBitmap.Height/4 + 1;

					// signal - turn green if allowed. Otherwise request green and it will go yellow the other way.
					if (square.SignalDirection != MapSquare.SIGNAL_DIRECTION.NONE)
					{
						// the /2 for half length and the other /2 because map units are 1/2 sprite units.
						bool carInInter = CarInIntersection(plyr, ptFrontNextTile, limoHalfLength);

						// We have to lock the signal once it is used. Otherwise car #1 gets a green and car #2 flips it to the other direction.
						bool signalLocked = signalsSet.Contains(ptFrontNextTile);

						// if the signal is not locked and we can only flip to yellow, wait in case the car will move out
						if ((!signalLocked) && carInInter && (!lastPass))
						{
							if (isDebugEnabled)
								log.Debug(string.Format("{0} : see if signal opens up, location = {1}", plyr.Name, plyr.Limo.Location));
							return;
						}

						// if the signal has not been locked yet, lock it.
						// we are here even if we are not stopped (have a green) - because we need to lock in the green.
						if (!signalLocked)
							signalsSet.Add(ptFrontNextTile);

						// if this is the first on this square, then we can set the signal.
						if (isStop && (!signalLocked))
						{
							if (carInInter)
							{
								// flip it to yellow so we get it when the car in there exits.
								switch (direction)
								{
									case MapSquare.COMPASS_DIRECTION.NORTH:
									case MapSquare.COMPASS_DIRECTION.SOUTH:
										square.SignalDirection = MapSquare.SIGNAL_DIRECTION.EAST_WEST_YELLOW;
										break;
									case MapSquare.COMPASS_DIRECTION.EAST:
									case MapSquare.COMPASS_DIRECTION.WEST:
										square.SignalDirection = MapSquare.SIGNAL_DIRECTION.NORTH_SOUTH_YELLOW;
										break;
								}
							}
							else
							{
								// all ours - flip to green (may already be green) and keep going
								isStop = false;
								switch (direction)
								{
									case MapSquare.COMPASS_DIRECTION.NORTH:
									case MapSquare.COMPASS_DIRECTION.SOUTH:
										square.SignalDirection = MapSquare.SIGNAL_DIRECTION.NORTH_SOUTH_GREEN;
										break;
									case MapSquare.COMPASS_DIRECTION.EAST:
									case MapSquare.COMPASS_DIRECTION.WEST:
										square.SignalDirection = MapSquare.SIGNAL_DIRECTION.EAST_WEST_GREEN;
										break;
								}
							}
							if (isDebugEnabled)
								log.Debug(string.Format("{0} : signal at {1} flipped to {2}", plyr.Name, ptFrontNextTile, square.SignalDirection));
						}
					}

					// see if we can go again. 
					if (isStop && (square.StopSigns != MapSquare.STOP_SIGNS.NONE) && (plyr.Limo.IsStopped))
					{
						bool carInInter = CarInIntersection(plyr, ptFrontNextTile, limoHalfLength + TileMovement.NUM_UNITS_CROSS_ROAD);
						if (!carInInter)
						{
							isStop = false;
							plyr.Limo.Go();
						}

						// wait and see if it clears out.
						if (isStop && (! lastPass))
						{
							if (isDebugEnabled)
								log.Debug(string.Format("{0} : see if stop opens up, location = {1}", plyr.Name, plyr.Limo.Location));
							ValidateData(plyr);
							return;
						}
					}

					// stop signs will always have this true
					if (isStop)
					{
						plyr.Limo.Stop();
						if (isDebugEnabled)
							log.Debug(string.Format("{0} : STOP, location = {1}", plyr.Name, plyr.Limo.Location));
						ValidateData(plyr);
						return;
					}

					// if we're making a left turn in an intersection, we have to look at future of other cars. We can only do this if they have
					// a path because the random direction is selected when the center croses into the new tile.
					if (plyr.Limo.PathTiles.Count > 0)
						switch (square.Tile.Direction)
						{
							case MapTile.DIRECTION.INTERSECTION:
							case MapTile.DIRECTION.T_NORTH:
							case MapTile.DIRECTION.T_EAST:
							case MapTile.DIRECTION.T_SOUTH:
							case MapTile.DIRECTION.T_WEST:
								Point ptNext = plyr.Limo.PathTiles[0];
								int indPath = 1;
								if (ptNext == ptFrontTile && indPath < plyr.Limo.PathTiles.Count)
									ptNext = plyr.Limo.PathTiles[indPath++];
								if (ptNext == ptFrontNextTile && indPath < plyr.Limo.PathTiles.Count)
									ptNext = plyr.Limo.PathTiles[indPath];
								if (ptFrontNextTile == ptNext)
								{
									if (isDebugEnabled)
										log.Debug(string.Format("{0} : ptFrontNextTile == ptNext == {1} (count = {2}", plyr.Name, ptNext,
											plyr.Limo.PathTiles.Count));
									break;
								}
								MapSquare.COMPASS_DIRECTION dirEnter = MapSquare.PointsDirection(ptFrontTile, ptFrontNextTile);
								MapSquare.COMPASS_DIRECTION dirExit = MapSquare.PointsDirection(ptFrontNextTile, ptNext);
								// fast exit for straight
								if (dirEnter == dirExit)
									break;
								// exit if right turn
								if ((dirEnter != MapSquare.COMPASS_DIRECTION.NORTH || dirExit != MapSquare.COMPASS_DIRECTION.WEST) &&
								    (dirEnter != MapSquare.COMPASS_DIRECTION.EAST || dirExit != MapSquare.COMPASS_DIRECTION.NORTH) &&
								    (dirEnter != MapSquare.COMPASS_DIRECTION.SOUTH || dirExit != MapSquare.COMPASS_DIRECTION.EAST) &&
								    (dirEnter != MapSquare.COMPASS_DIRECTION.WEST || dirExit != MapSquare.COMPASS_DIRECTION.SOUTH))
									break;

								// ok it's a left or U turn - need to check oncoming traffic
								if (isDebugEnabled)
									log.Debug(string.Format("{0} : making left turn {1} to {2} (ptNext = {3})", plyr.Name, dirEnter, dirExit,
										ptNext));

								// check the future of other cars - see if it is anywhere in the square. Distance is MaxTileSteps steps through the curve
								// at speed MAX_CURVE_SPEED while the oncoming is at MAX_STRAIGHT_SPEED. Plus the car from center to front * 3/4 for car plus space between
								int safeDistance = (plyr.Limo.VehicleBitmap.Height*3)/4 + 1 +
								                   ((TileMovement.MAX_TILE_STEPS + TileMovement.MAX_CURVE_SPEED - 1)/
								                    TileMovement.MAX_CURVE_SPEED)*TileMovement.MAX_STRAIGHT_SPEED;
								foreach (Player plyrOn in Players.Where(plyrOn => plyrOn != plyr))
								{
									// if they're more than the Limo.NUM_FUTURE away, then no need to walk as it can't match.
									if ((Math.Abs(plyr.Limo.Location.MapPosition.X - plyrOn.Limo.Location.MapPosition.X) >
									     safeDistance + TileMovement.UNITS_PER_TILE) ||
									    (Math.Abs(plyr.Limo.Location.MapPosition.Y - plyrOn.Limo.Location.MapPosition.Y) >
									     safeDistance + TileMovement.UNITS_PER_TILE))
										continue;
									// if they're stopped, not an issue
									if (plyrOn.Limo.IsStopped && BoardLocation.MapToTile(plyrOn.Limo.Front) != ptFrontNextTile)
									{
										if (isDebugEnabled)
											log.Debug(string.Format("{0} : player {1} limo stopped - ignore for turn check", plyr.Name, plyrOn.Name));
										continue;
									}

									// see if anyone's future goes into this quarter tile (we use quarter tiles for collision detection).
									int numSteps = safeDistance;
									if (isDebugEnabled)
										log.Debug(string.Format("{0} : check {1} oncoming into tile {2}, safeDist = {3}", plyr.Name, plyrOn.Name,
											ptFrontNextTile, safeDistance));

									// we first check for the cent of the limo in this tile. This handles cars turning (which will end up cross and not catch in the below foreach).
									// this also handles any case of any car in the intersection for any reason - we don't turn then because that would be a conflict for an
									// outer curve (exception being an inverse inner curve - but tough).
									bool waitForOncoming = plyrOn.Limo.Location.TilePosition == ptFrontNextTile;

									if (! waitForOncoming)
										foreach (Limo.QuarterSteps stepOn in plyrOn.Limo.Future)
										{
											if (isDebugEnabled)
												log.Debug(string.Format("{0} : stepOn = {1} ({2})", plyr.Name, stepOn,
													BoardLocation.QuarterTileToTile(stepOn.QuarterLocation)));
											if (BoardLocation.QuarterTileToTile(stepOn.QuarterLocation) == ptFrontNextTile)
											{
												// they must be coming from the opposite direction to matter. If coming from the left/right
												// they are going to hit a signal or stoplight. We need the next tile out to get direction (next quarter tile can be diaganol).
												Point ptCenterPlyrOn = plyrOn.Limo.Location.TilePosition;
												MapSquare.COMPASS_DIRECTION dirPlyrOn = MapSquare.COMPASS_DIRECTION.NONE;
												foreach (Limo.QuarterSteps qs in plyrOn.Limo.Future)
												{
													Point ptFutureTile = BoardLocation.QuarterTileToTile(qs.QuarterLocation);
													if (ptFutureTile != ptCenterPlyrOn)
													{
														dirPlyrOn = MapSquare.PointsDirection(ptCenterPlyrOn, ptFutureTile);
														break;
													}
												}
												Trap.trap(dirPlyrOn == MapSquare.COMPASS_DIRECTION.NONE);
												if (dirPlyrOn != MapSquare.COMPASS_DIRECTION.NONE && MapSquare.UTurn(dirPlyrOn) != dirEnter)
												{
													if (isDebugEnabled)
														log.Debug(string.Format("{0} : player {1} dir from side {2} - ignore for turn check", plyr.Name,
															plyrOn.Name, dirPlyrOn));
													break;
												}
												waitForOncoming = true;
												break;
											}

											// see if gone enough steps
											numSteps -= stepOn.Steps;
											if (numSteps <= 0)
												break;
										}

									if (!waitForOncoming)
										continue;

									// wait and see if it moves
									if ((!lastPass) && (plyrOn.Limo.StepsRemaining > 0))
									{
										if (isDebugEnabled)
											log.Debug(string.Format("{0} : left turn wait - behind {1}, location = {2}", plyr.Name, plyrOn.Name,
												plyrOn.Limo.Location));
										ValidateData(plyr);
										return;
									}

									// need to stop
									plyr.Limo.Stop();
									if (isDebugEnabled)
										log.Debug(string.Format("{0} : left turn stop because of {1}, location = {2}", plyr.Name, plyrOn.Name,
											plyrOn.Limo.Location));
									ValidateData(plyr);
									return;
								}
								break;
						}
					else if (isDebugEnabled)
						log.Debug(string.Format("{0} : path.count == 0", plyr.Name));
				}

				// is there anyone in our way? 1.5 is center to front plus 1 car length. Additional /2 is because map units are 1/2 sprite units.
				if (!plyr.Limo.IsForceMove)
				{
					int safeDistance = (plyr.Limo.VehicleBitmap.Height*3)/4 + 1;
					foreach (Player plyrOn in Players.Where(plyrOn => plyrOn != plyr))
					{
						// if they're more than the Limo.NUM_FUTURE + Limo.NUM_TAIL away, then no need to walk as it can't match.
						if ((Math.Abs(plyr.Limo.Location.MapPosition.X - plyrOn.Limo.Location.MapPosition.X) >
						     Limo.NUM_FUTURE + Limo.NUM_TAIL + TileMovement.UNITS_PER_TILE) ||
						    (Math.Abs(plyr.Limo.Location.MapPosition.Y - plyrOn.Limo.Location.MapPosition.Y) >
						     Limo.NUM_FUTURE + Limo.NUM_TAIL + TileMovement.UNITS_PER_TILE))
							continue;

						// see if we hit anyone's tail with our future
						int stepsRemaining = safeDistance;
						foreach (Limo.QuarterSteps stepOn in plyr.Limo.Future)
						{
							// if the car is 1/4 speed or stopped and we can fully pass it when it goes back to full speed, we move through it.
							bool slowDown = plyrOn.Limo.TailQuarter.Contains(stepOn.QuarterLocation);
							if (slowDown && (plyrOn.Limo.IsFlatTire || (plyrOn.Limo.IsQuarterSpeed && (! plyr.Limo.IsQuarterSpeed))))
							{
								// the +1 after each div is to handle the remainder in the divisions.
								int stepsToLeader = Math.Abs(plyr.Limo.Location.MapPosition.X - plyrOn.Limo.Location.MapPosition.X) +
								                   Math.Abs(plyr.Limo.Location.MapPosition.Y - plyrOn.Limo.Location.MapPosition.Y);
								int stepsToPass = (int) ((Limo.NUM_TAIL * 2) / (plyr.Limo.MaxSpeed - plyrOn.Limo.MaxSpeed)) + 1;
								// the steps to join and then be a car length ahead.
								// The MAX_STRAIGHT_SPEED because it's Speed / MAX_STRAIGHT_SPEED per step.
								int stepsToClear = (stepsToLeader + stepsToPass) * TileMovement.MAX_STRAIGHT_SPEED + 1;
								if (plyrOn.Limo.IsFlatTire && (stepsToClear < plyrOn.Limo.TickCountToFixFlat))
									slowDown = false;
								if (plyrOn.Limo.IsQuarterSpeed && (stepsToClear < plyrOn.TicksToFullSpeed))
									slowDown = false;
							}

							// see if both are in the same quarter tile. We use quarter tiles for collision detection.
							if (slowDown)
							{
								// wait and see if it moves
								if ((!lastPass) && (plyrOn.Limo.StepsRemaining > 0))
								{
									if (isDebugEnabled)
										log.Debug(string.Format("{0} : wait - behind {1}, location = {2}", plyr.Name, plyrOn.Name,
											plyrOn.Limo.Location));
									ValidateData(plyr);
									return;
								}

								// next turn will +0.5 so this will have a final affect of -0.5
								plyr.Limo.Decelerate();
								plyr.Limo.NumTurnsStopped++;
								if (isDebugEnabled)
									log.Debug(string.Format("{0} : stuck behind {1}, location = {2}", plyr.Name, plyrOn.Name, plyrOn.Limo.Location));
								ValidateData(plyr);
								return;
							}

							// see if we've walked far enough.
							stepsRemaining -= stepOn.Steps;
							if (stepsRemaining <= 0)
								break;
						}
					}
				}

				// if the next center position is in the same tile, we can move along the offset
				if (plyr.Limo.Location.IsMoveInsideTile)
				{
					plyr.Limo.Move();
					plyr.Limo.StepsRemaining--;
					if (isDebugEnabled)
						log.Debug(string.Format("{0} : move within tile, location = {1}", plyr.Name, plyr.Limo.Location));
					Trap.trap(ptCenterNextMapPos != plyr.Limo.Location.MapPosition);
					ValidateData(plyr);

					// have we arrived at a bus stop - needs to be the center point.
					Point ptCenterTile = plyr.Limo.Location.TilePosition;
					MapSquare squareCenter = MainMap.Squares[ptCenterTile.X][ptCenterTile.Y];
					bool centerOfTile = plyr.Limo.Location.OffsetTileMoves == plyr.Limo.Location.TileMoves.Moves.Length/2;
					if ((squareCenter.Tile.Type == MapTile.TYPE.BUS_STOP) &&centerOfTile)
					{
						BusStopOffOn(plyr, ptCenterTile);
						plyr.Limo.Stop();
						plyr.Limo.Go();
						if (isDebugEnabled)
							log.Debug(string.Format("{0} : bus stop processed limo = {1}", plyr.Name, plyr.Limo));
						Trap.trap(ptCenterNextMapPos != plyr.Limo.Location.MapPosition);
						ValidateData(plyr);
					}

					// have we arrived at a coffee store - needs to be the center point.
					if ((squareCenter.Tile.Type == MapTile.TYPE.COFFEE_STOP) && centerOfTile)
					{
						CoffeeStopFillUp(plyr, ptCenterTile);
						plyr.Limo.Stop();
						plyr.Limo.Go();
						if (isDebugEnabled)
							log.Debug(string.Format("{0} : coffee stop processed limo = {1}", plyr.Name, plyr.Limo));
						Trap.trap(ptCenterNextMapPos != plyr.Limo.Location.MapPosition);
						ValidateData(plyr);
					}

					continue;
				}

				if (isDebugEnabled)
					log.Debug(string.Format("{0} : before move 1 to next tile, location = {1}", plyr.Name, plyr.Limo.Location));

				// we move to the next spot (start of next tile), then below assign movement within the tile
				plyr.Limo.Move();
				plyr.Limo.StepsRemaining--;

				if (isDebugEnabled)
					log.Debug(string.Format("{0} : after move 1 to next tile, location = {1}", plyr.Name, plyr.Limo.Location));
				Trap.trap(plyr.Limo.Location.OffsetTileMoves != plyr.Limo.Location.TileMoves.Moves.Length);
				Trap.trap(ptCenterNextMapPos != plyr.Limo.Location.MapPosition);

				// we now work with the next position because that moves us into the next tile if we are at offset 0 and going north or west.
				// this has to be the next map position converted to tile position because that is sometimes this tile position, sometimes next.
				ptCenterNextMapPos = plyr.Limo.Location.NextPosition;
				Point ptCenterNextTile = BoardLocation.MapToTile(ptCenterNextMapPos);

				if (isDebugEnabled)
					log.Debug(string.Format("{0}, ptCenterNextTile={1}, path={2}", plyr.Name, ptCenterNextTile, string.Join(", ", plyr.Limo.PathTiles)));

				// discard next tile(s) - if it has it and if it's the one we're entering.
				int indexPath = plyr.Limo.PathTiles.FindIndex(pt => pt == ptCenterNextTile);
				if (indexPath != -1)
				{
					if (log.IsDebugEnabled)
						log.Debug(string.Format("path.RemoveRange(0, {0})", indexPath + 1));
					plyr.Limo.PathTiles.RemoveRange(0, indexPath + 1);
				}

				// get our new destination - from the path if valid
				MapSquare.COMPASS_DIRECTION destDirection = MapSquare.COMPASS_DIRECTION.NONE;
				if (plyr.Limo.PathTiles.Count > 0)
				{
					int x = plyr.Limo.PathTiles[0].X - ptCenterNextTile.X;
					int y = plyr.Limo.PathTiles[0].Y - ptCenterNextTile.Y;
					if (Math.Abs(x) + Math.Abs(y) == 1)
					{
						if (MainMap.Squares[plyr.Limo.PathTiles[0].X][plyr.Limo.PathTiles[0].Y].Tile.IsDriveable)
						{
							destDirection = MapSquare.PointsDirection(ptCenterNextTile, plyr.Limo.PathTiles[0]);
							if (isDebugEnabled)
								log.Debug(string.Format("{0} : {1} = {2} - {3}", plyr.Name, destDirection, ptCenterNextTile,
									plyr.Limo.PathTiles[0]));
						}
						else
						{
							Trap.trap();
							plyr.GameStatus(null, plyr, PlayerAIBase.STATUS.NO_PATH, Players, Passengers);
							if (log.IsWarnEnabled)
								log.Warn(string.Format("{0} has non-driveable path {1} location: {2}", plyr.Name, plyr.Limo.PathTiles[0], plyr.Limo.Location));
							plyr.Limo.PathTiles.Clear();
						}
					}
					else
					{
						Trap.trap();
						plyr.GameStatus(null, plyr, PlayerAIBase.STATUS.NO_PATH, Players, Passengers);
						if (log.IsWarnEnabled)
							log.Warn(string.Format("{0} can't get to path {1} location: {2}", plyr.Name, plyr.Limo.PathTiles[0], plyr.Limo.Location));
						plyr.Limo.PathTiles.Clear();
					}
				}

				// need to grab one of the exit tiles at random. U-turn only if no alternative
				bool noPath = false;
				if (destDirection == MapSquare.COMPASS_DIRECTION.NONE)
				{
					Trap.trap(plyr.Limo.PathTiles.Count > 0);
					destDirection = square.Tile.GetRandomNext(direction);
					noPath = true;
					if (log.IsWarnEnabled)
						log.Warn(string.Format("{0} : {1} = GetRandomNext({2})", plyr.Name, destDirection, direction));
				}

				// get the inter-tile movement and assign
				TileMovement movement = TileMovement.GetMove(direction, destDirection);
				plyr.Limo.Location.SetMapPosition(plyr.Limo.Location.MapPosition, movement);

				// do we have to slow down?
				if (plyr.Limo.MaxSpeed < plyr.Limo.Speed)
				{
					float diff = plyr.Limo.Speed - plyr.Limo.MaxSpeed;
					plyr.Limo.Speed = plyr.Limo.MaxSpeed;
					plyr.Limo.StepsRemaining = Math.Max(0, plyr.Limo.StepsRemaining - (int) (diff + 0.9f));
				}

				// tell the player we need a new path.
				if (noPath)
					plyr.GameStatus(null, plyr, PlayerAIBase.STATUS.NO_PATH, Players, Passengers);

				if (isDebugEnabled)
					log.Debug(string.Format("{0} : next tile complete, location = {1}", plyr.Name, plyr.Limo.Location));
				ValidateData(plyr);
			}
		}

		private void BusStopOffOn(Player plyr, Point busStop)
		{
			Company cmpny = companies.FirstOrDefault(cpy => cpy.BusStop == busStop);
			if (cmpny == null)
			{
				if (log.IsInfoEnabled)
					log.Info(string.Format("{0} enters bus stop {1} which is NOT a company", plyr.Name, busStop));
				return;
			}

			if (log.IsDebugEnabled)
			{
				log.Debug(string.Format("{0} enters bus stop {1} with {2}", plyr.Name, cmpny.Name,
					plyr.Passenger == null ? "{none}" : plyr.Passenger.Name));
				if (cmpny.Passengers.Count > 0)
					log.Info(string.Format("       In lobby: {0}", string.Join(", ", cmpny.Passengers.Select(cpy => cpy.Name).ToArray())));
				if (plyr.PickUp.Count > 0)
					log.Info(string.Format("       Pick-up request: {0}", string.Join(", ", plyr.PickUp.Select(psngr => psngr.Name).ToArray())));
			}

			// now all cards can be played because we've hit a stop. The player can't send the command to play them until
			// we leave this method and complete the turn processing so no card flipping to OK can be used for this passenger
			// picked up.
			foreach (var cardOn in plyr.PowerUpsDrawn)
				cardOn.OkToPlay = true;

			// drop off if we have one and can do so
			PlayerAIBase.STATUS status = PlayerAIBase.STATUS.PASSENGER_NO_ACTION;
			Passenger psngrAbandoned = null;
			if (plyr.Passenger != null)
			{
				// Any enemies here?
				bool noDrop = cmpny.Passengers.Any(psngr => psngr.Lobby == cmpny && plyr.Passenger.Enemies.Contains(psngr));
				if (noDrop)
				{
					status = PlayerAIBase.STATUS.PASSENGER_REFUSED_ENEMY;
					InfoAndStatus(string.Format("{0} could not drop off passenger {1} at {2} - enemy", plyr.Name, plyr.Passenger.Name, cmpny.Name));
				}
				else
				{
					plyr.Limo.CoffeeServings--;

					if (plyr.Passenger.Destination == cmpny)
					{
						float points = plyr.Passenger.PointsDelivered;
						InfoAndStatus(string.Format("{0} dropped off passenger {1} at {2}", plyr.Name, plyr.Passenger.Name, cmpny.Name));
						plyr.Delivered(plyr.Passenger);
						plyr.Passenger.Arrived(cmpny);
						cmpny.Passengers.Add(plyr.Passenger);
						plyr.Passenger = null;
						status = PlayerAIBase.STATUS.PASSENGER_DELIVERED;

						// handle power-up multipliers
						if (plyr.PowerUpThisTransit != null)
						{
							switch (plyr.PowerUpThisTransit.Card)
							{
								case PowerUp.CARD.MULT_DELIVERY_QUARTER_SPEED:
									plyr.AddPoints(points * 0.5f);
									break;
								case PowerUp.CARD.MULT_DELIVER_AT_COMPANY:
									plyr.AddPoints(points * 0.2f);
									break;
								case PowerUp.CARD.MULT_DELIVERING_PASSENGER:
									plyr.AddPoints(points * 0.2f);
									break;
							}
						}
					}
					else
					{
						InfoAndStatus(string.Format("{0} abandoned passenger {1} at {2}", plyr.Name, plyr.Passenger.Name, cmpny.Name));
						psngrAbandoned = plyr.Passenger;
						plyr.Passenger.Abandoned(cmpny);
						cmpny.Passengers.Add(plyr.Passenger);
						plyr.Passenger = null;
						status = PlayerAIBase.STATUS.PASSENGER_ABANDONED;
					}

					// either way, this is over so powerup is discarded and speed adjusted.
					if (plyr.PowerUpThisTransit != null)
					{
						// used an extra coffee
						plyr.Limo.CoffeeServings--;
					
						// back to regular speed - if not in the all cars 1/4 mode
						if (plyr.PowerUpThisTransit.Card == PowerUp.CARD.MULT_DELIVERY_QUARTER_SPEED && plyr.TicksToFullSpeed <= 0)
							plyr.Limo.IsQuarterSpeed = false;
						plyr.PowerUpThisTransit = null;
					}
				}
			}

			// pick up if we have none (check because drop off may have been disallowed).
			// Can't be one we completed and can't be the one we dropped above.
			if (plyr.Passenger == null)
			{
				Passenger passenger =
					plyr.PickUp.FirstOrDefault(
						psngr =>
							(psngr.Lobby == cmpny) && (psngr.Car == null) && (! plyr.PassengersDelivered.Contains(psngr)) &&
							(psngr != psngrAbandoned));
				if (passenger != null)
				{
					// do we need to restock the coffee first?
					if (plyr.Limo.CoffeeServings <= 0)
					{
						InfoAndStatus(string.Format("{0} could not pick up passenger at {1} - no coffee", plyr.Name, cmpny.Name));
						status = status == PlayerAIBase.STATUS.PASSENGER_NO_ACTION
							? PlayerAIBase.STATUS.PASSENGER_REFUSED_NO_COFFEE
							: PlayerAIBase.STATUS.PASSENGER_DELIVERED_AND_PICK_UP_REFUSED;
					}
					else
					{
						InfoAndStatus(string.Format("{0} picked up passenger {1} at {2}", plyr.Name, passenger.Name, cmpny.Name));

						passenger.EnterCar(plyr.Limo);
						plyr.Passenger = passenger;
						plyr.PickUp.Remove(passenger);
						cmpny.Passengers.Remove(passenger);
						status = status == PlayerAIBase.STATUS.PASSENGER_NO_ACTION
							? PlayerAIBase.STATUS.PASSENGER_PICKED_UP
							: PlayerAIBase.STATUS.PASSENGER_DELIVERED_AND_PICKED_UP;

						// handle the power-up - check for correct passenger/destination, assign it if ok
						if (plyr.PowerUpNextBusStop != null)
						{
							// need an extra coffee for the cards
							if (plyr.Limo.CoffeeServings <= 1)
							{
								if (log.IsWarnEnabled)
									log.Warn(string.Format("ERROR card {0} - player {1} cannot play card - not enough coffee", 
													plyr.PowerUpNextBusStop.Card, plyr.Name));
								plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpNextBusStop);
							}

							else
							{
								plyr.PowerUpThisTransit = plyr.PowerUpNextBusStop;
								plyr.PowerUpNextBusStop = null;
								switch (plyr.PowerUpThisTransit.Card)
								{
									case PowerUp.CARD.MULT_DELIVERY_QUARTER_SPEED:
										plyr.Limo.IsQuarterSpeed = true;
										InfoAndStatus(string.Format("{0} transiting passenger with card MULT_DELIVERY_QUARTER_SPEED", plyr.Name));
										foreach (Player plyrOn in Players)
											plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpThisTransit);
										break;
									case PowerUp.CARD.MULT_DELIVER_AT_COMPANY:
										// is the destination correct?
										if (plyr.PowerUpThisTransit.Company != plyr.Passenger.Destination)
										{
											if (log.IsWarnEnabled)
												log.Warn(
													string.Format(
														"ERROR card MULT_DELIVER_AT_COMPANY - player {0} card destination {1} != passenger destination {2}",
														plyr.Name, plyr.PowerUpThisTransit.Company.Name, plyr.Passenger.Destination.Name));
											plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpThisTransit);
											plyr.PowerUpThisTransit = null;
											break;
										}
										InfoAndStatus(string.Format("{0} transiting passenger {1} with card MULT_DELIVER_AT_COMPANY to company {2}",
											plyr.Name, plyr.Passenger.Name, plyr.Passenger.Destination));
										foreach (Player plyrOn in Players)
											plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpThisTransit);
										break;
									case PowerUp.CARD.MULT_DELIVERING_PASSENGER:
										// is the passenger correct?
										if (plyr.PowerUpThisTransit.Passenger != plyr.Passenger)
										{
											if (log.IsWarnEnabled)
												log.Warn(
													string.Format("ERROR card MULT_DELIVERING_PASSENGER - player {0} card passenger {1} != passenger {2}",
														plyr.Name, plyr.PowerUpThisTransit.Passenger.Name, plyr.Passenger.Name));
											plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_ILLEGAL_TO_PLAY, plyr.PowerUpThisTransit);
											plyr.PowerUpThisTransit = null;
											break;
										}
										InfoAndStatus(string.Format("{0} transiting passenger {1} with card MULT_DELIVERING_PASSENGER to company {1}",
											plyr.Name, plyr.PowerUpThisTransit.Passenger.Name));
										foreach (Player plyrOn in Players)
											plyrOn.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_PLAYED, plyr.PowerUpThisTransit);
										break;
									default:
										// should never happen
										Trap.trap();
										plyr.PowerUpThisTransit = null;
										break;
								}
							}
						}
					}
				}
			}

			// we check for too many powerups (due to points from delivering above)
			if (plyr.PowerUpsDrawn.Count > plyr.MaxPowerUpsInHand)
			{
				PowerUp pu = null;
				while (plyr.PowerUpsDrawn.Count > plyr.MaxPowerUpsInHand)
				{
					pu = plyr.PowerUpsDrawn.OrderBy(p => rand.Next()).First();
					plyr.PowerUpsDrawn.Remove(pu);
				}
				if (log.IsWarnEnabled)
					log.Warn(string.Format("{0} has too many cards in hand. Discards include {1}", plyr.Name, pu));
				plyr.GameStatus(plyr, PlayerAIBase.STATUS.POWER_UP_HAND_TOO_MANY, pu);
			}

			if (log.IsDebugEnabled)
			{
				log.Debug(string.Format("{0} exits bus stop {1} with {2}", plyr.Name, cmpny.Name,
					plyr.Passenger == null ? "{none}" : plyr.Passenger.Name));
				if (cmpny.Passengers.Count > 0)
					log.Info(string.Format("       In lobby: {0}", string.Join(",", cmpny.Passengers.Select(cpy => cpy.Name).ToArray())));
				if (plyr.PickUp.Count > 0)
					log.Info(string.Format("       Pickup list: {0}", string.Join(",", plyr.PickUp.Select(psngr => psngr.Name).ToArray())));
			}

			// Tell all players (and reset when next general status update will occur) 
			framework.ticksSinceLastUpdate = 0;
			foreach (Player plyrOn in Players)
				plyrOn.GameStatus(null, plyr, status, Players, Passengers);
			if (!framework.FullSpeed)
			{
				framework.mainWindow.UpdatePlayers();
				framework.mainWindow.UpdateDebug();
			}
		}

		private void CoffeeStopFillUp(Player plyr, Point busStop)
		{

			CoffeeStore store = stores.FirstOrDefault(cpy => cpy.BusStop == busStop);
			if (store == null)
			{
				Trap.trap();
				if (log.IsInfoEnabled)
					log.Info(string.Format("{0} enters map square {1} which is NOT a coffee store", plyr.Name, busStop));
				return;
			}

			// now all cards can be played because we've hit a stop. 
			foreach (var cardOn in plyr.PowerUpsDrawn)
				cardOn.OkToPlay = true;

			// can't stock up if have a passenger
			if (plyr.Passenger != null)
			{
				if (log.IsInfoEnabled)
					log.Info(string.Format("ERROR: no restock - player {0} enters coffee store {1} with passenger {2}", plyr.Name, store.Name, plyr.Passenger.Name));
				plyr.GameStatus(null, plyr, PlayerAIBase.STATUS.COFFEE_STORE_NO_STOCK_UP, Players, Passengers);
				return;
			}

			plyr.Limo.CoffeeServings = 3;
			InfoAndStatus(string.Format("{0} enters coffee store {1} and restocks car", plyr.Name, store.Name));

			// Tell all players (and reset when next general status update will occur) 
			framework.ticksSinceLastUpdate = 0;
			foreach (Player plyrOn in Players)
				plyrOn.GameStatus(null, plyr, PlayerAIBase.STATUS.COFFEE_STORE_CAR_RESTOCKED, Players, Passengers);
			if (!framework.FullSpeed)
			{
				framework.mainWindow.UpdatePlayers();
				framework.mainWindow.UpdateDebug();
			}
		}

		/// <summary>
		///     Determine if any other limos are in this intersection.
		/// </summary>
		/// <param name="plyr">The player checking for. Ok if this player's car is in the intersection.</param>
		/// <param name="ptIntersection">The intersection tile. This is in tile units.</param>
		/// <param name="numFutureLook">How many steps into the future to look.</param>
		/// <returns></returns>
		private bool CarInIntersection(Player plyr, Point ptIntersection, int numFutureLook)
		{
			// bugbug numFutureLook (elsewhere too) depends on left/right/straight
			foreach (Player plyrOn in Players.Where(plyrOn => plyrOn != plyr))
			{
				// if they're more than the numFutureLook + limo size (Bitmap/2) away, then no need to walk as it can't match.
				if ((Math.Abs(plyr.Limo.Location.MapPosition.X - plyrOn.Limo.Location.MapPosition.X) >
				     numFutureLook + TileMovement.UNITS_PER_TILE) ||
				    (Math.Abs(plyr.Limo.Location.MapPosition.Y - plyrOn.Limo.Location.MapPosition.Y) >
				     numFutureLook + TileMovement.UNITS_PER_TILE))
					continue;

				// we walk numFutureLook (or till Future runs out) steps in the future to see if it's in the intersection.
				int stepsRemaining = numFutureLook;
				foreach (Limo.QuarterSteps stepOn in plyrOn.Limo.Future)
				{
					if (BoardLocation.QuarterTileToTile(stepOn.QuarterLocation) == ptIntersection)
					{
						if (log.IsDebugEnabled)
							log.Debug(string.Format("{0} : Player {1} Future in intersection {2}", plyr.Name, plyrOn.Name,
								ptIntersection));
						return true;
					}
					stepsRemaining -= stepOn.Steps;
					if (stepsRemaining <= 0)
						break;
				}

				// checking the tail
				if (plyrOn.Limo.TailQuarter.Any(qtrOn => BoardLocation.QuarterTileToTile(qtrOn) == ptIntersection))
				{
					// bus stop (or any u-turn), one car wants out, the other pulls in where it has 1 map unit tail still in the intersection.
					// so if both vehicle center's are in the same tile AND they are in oppisate directions, then we say not in the intersection
					var tile = MainMap.SquareOrDefault(ptIntersection).Tile;
					if (tile.IsDriveable)
						if (tile.Direction == MapTile.DIRECTION.NORTH_UTURN || tile.Direction == MapTile.DIRECTION.EAST_UTURN ||
						    tile.Direction == MapTile.DIRECTION.SOUTH_UTURN || tile.Direction == MapTile.DIRECTION.WEST_UTURN)
						{
							Trap.trap();
							if (plyrOn.Limo.Location.Direction == MapSquare.UTurn(plyr.Limo.Location.Direction))
							{
								Trap.trap();
								continue;
							}
							Trap.trap();
						}


					if (log.IsDebugEnabled)
						log.Debug(string.Format("{0} : Player {1} Future in intersection {2}", plyr.Name, plyrOn.Name,
							ptIntersection));
					return true;
				}
				if (log.IsDebugEnabled)
					log.Debug(string.Format("{0} : Player {1} NOT in intersection {2}", plyr.Name, plyrOn.Name, ptIntersection));
			}
			return false;
		}

		#endregion

		#region AI API

		public void PlayerAddOrder(string myPlayerGuid, List<Point> path, List<Passenger> pickUp)
		{
			Player player = Players.Find(pl => pl.Guid == myPlayerGuid);
			RemoteMessages.Enqueue(new AiPathMessage(player, path, pickUp));
		}

		/// <summary>
		/// Send the engine a power-up card command.
		/// </summary>
		/// <param name="playerGuid">the player sending the message.</param>
		/// <param name="action">The action to take with the card.</param>
		/// <param name="card">The card to perform the action on.</param>
		public void PlayerPowerupsEvent(string playerGuid, PlayerAIBase.CARD_ACTION action, PowerUp card)
		{
			Player player = Players.Find(pl => pl.Guid == playerGuid);
			RemoteMessages.Enqueue(new AiPowerupMessage(player, action, card));
		}

		internal void PlayerOrders(Player player, List<Point> path, List<Passenger> pickUp)
		{

			if (log.IsDebugEnabled)
			{
				log.Debug(string.Format("player {0} before handle orders location: {1}", player.Name, player.Limo.Location));
				log.Debug(string.Format("     player path: {0}", string.Join(", ", player.Limo.PathTiles)));
				log.Debug(string.Format("     orders path: {0}", string.Join(", ", path)));
			}

			// sync up - try to find a node in the existing path that matches
			if (path.Count > 0)
			{
				Point carPosition = player.Limo.Location.TilePosition;
				Point carNextPosition = player.Limo.Location.NextTilePosition;
				// 1. if we can find the next position in the passed in path, we go from there
				int indStart = path.FindIndex(p => p == carNextPosition);
				if (indStart != -1)
				{
					player.Limo.PathTiles.Clear();
					player.Limo.PathTiles.AddRange(path.Skip(indStart));
					if (log.IsDebugEnabled)
						log.Debug("     clear, add path");
				}
				else
				{
					// 2. if we can find the present position, we can go to the next then U-turn back to this.
					indStart = path.FindIndex(p => p == carPosition);
					if (indStart != -1)
					{
						player.Limo.PathTiles.Clear();
						player.Limo.PathTiles.Add(carNextPosition);
						player.Limo.PathTiles.AddRange(path.Skip(indStart));
						if (log.IsDebugEnabled)
							log.Debug("     clear, add carNextPosition, path");
					}
					else
					{
						// 3. look for existing path has a match with the start of the new path (ie the new path is for 
						//    when this path has gotten a little further).
						indStart = player.Limo.PathTiles.FindIndex(p => p == path[0]);
						if (indStart != -1)
						{
							player.Limo.PathTiles.RemoveRange(indStart, player.Limo.PathTiles.Count - indStart);
							player.Limo.PathTiles.AddRange(path);
							if (log.IsDebugEnabled)
								log.Debug(string.Format("     join paths at {0}", indStart));
						}
						else
						{
							// 4. get the path from carNextPosition to the new path
							List<Point> joinPath = SimpleAStar.CalculatePath(MainMap, carNextPosition, path[0]);
							if (log.IsDebugEnabled)
								log.Debug(string.Format("     create join path: {0}", string.Join(", ", joinPath)));
							if (joinPath.Count > 0)
							{
								// check join point not duplicated
								player.Limo.PathTiles.Clear();
								player.Limo.PathTiles.AddRange(joinPath);
								player.Limo.PathTiles.AddRange(path.Skip(1));
		
							}

							// and ask the AI for a new path as our created path may be off.
							player.GameStatus(null, player, PlayerAIBase.STATUS.NO_PATH, Players, Passengers);
							if (log.IsWarnEnabled)
								log.Warn(string.Format("     tried to join from carNextPosition to path. joinPath: {0}", string.Join(", ", joinPath)));
						}
					}
				}

				// for the status window - what company are we headed to
				for (int ind = 1; ind < player.Limo.PathTiles.Count; ind++)
				{
					Company cmpy = companies.FirstOrDefault(cpy => cpy.BusStop == player.Limo.PathTiles[ind]);
					if (cmpy != null)
					{
						player.NextBusStop = cmpy;
						break;
					}
				}
			}

			if (log.IsDebugEnabled)
			{
				log.Debug(string.Format("player {0} after handle orders location: {1}", player.Name, player.Limo.Location));
				log.Debug(string.Format("     player path: {0}", string.Join(", ", player.Limo.PathTiles)));
			}

			if (pickUp.Count <= 0)
				return;
			// new pick-up list.
			player.PickUp.Clear();
			foreach (Passenger psngrOn in pickUp.Where(psngrOn => (!player.PassengersDelivered.Contains(psngrOn))))
				player.PickUp.Add(psngrOn);
		}

		public void PlayerPowerups(Player player, PlayerAIBase.CARD_ACTION action, PowerUp card)
		{

			try
			{
				if (action == PlayerAIBase.CARD_ACTION.DISCARD)
				{
					player.PowerUpsDrawn.Remove(card);
					if (log.IsDebugEnabled)
						log.Debug(string.Format("{0} discards card {1}", player.Name, card.Name));
					return;
				}

				if (action == PlayerAIBase.CARD_ACTION.DRAW)
				{
					if (player.PowerUpsDrawn.Count < player.MaxPowerUpsInHand)
					{
						player.PowerUpsDeck.Remove(card);
						card.OkToPlay = Math.Abs(player.Score) < 0.01;
						player.PowerUpsDrawn.Add(card);
						if (log.IsDebugEnabled)
							log.Debug(string.Format("{0} draws card {1}", player.Name, card.Name));
						return;
					}
					if (log.IsWarnEnabled)
						log.Warn(string.Format("{0} cannot draw card {1} (too many in hand)", player.Name, card.Name)); 
					player.GameStatus(player, PlayerAIBase.STATUS.POWER_UP_DRAW_TOO_MANY, card);
					return;
				}

				// play
				if (! player.PowerUpsDrawn.Contains(card))
				{
					if (log.IsWarnEnabled)
						log.Warn(string.Format("{0} cannot play card {1} (not in hand)", player.Name, card.Name));
					player.GameStatus(player, PlayerAIBase.STATUS.POWER_UP_PLAY_NOT_EXIST, card);
					return;
				}

				if (! card.OkToPlay)
				{
					if (log.IsWarnEnabled)
						log.Warn(string.Format("{0} cannot play card {1} (not OK yet)", player.Name, card.Name));
					player.GameStatus(player, PlayerAIBase.STATUS.POWER_UP_PLAY_NOT_READY, card);
					return;
				}

				if (log.IsDebugEnabled)
					log.Debug(string.Format("{0} plays (submits to engine) card {1}", player.Name, card.Name));
				switch (card.Card)
				{
					case PowerUp.CARD.ALL_OTHER_CARS_QUARTER_SPEED:
					case PowerUp.CARD.CHANGE_DESTINATION:
					case PowerUp.CARD.MOVE_PASSENGER:
					case PowerUp.CARD.RELOCATE_ALL_CARS:
					case PowerUp.CARD.RELOCATE_ALL_PASSENGERS:
					case PowerUp.CARD.STOP_CAR:
						player.PowerUpInAction = card;
						break;
					case PowerUp.CARD.MULT_DELIVERING_PASSENGER:
					case PowerUp.CARD.MULT_DELIVERY_QUARTER_SPEED:
					case PowerUp.CARD.MULT_DELIVER_AT_COMPANY:
						player.PowerUpNextBusStop = card;
						break;
				}
				player.PowerUpsDrawn.Remove(card);
			}
			finally
			{
				if (!framework.FullSpeed)
				{
					framework.mainWindow.UpdatePlayers();
					framework.mainWindow.UpdateDebug();
				}
			}
		}

		#endregion

		#region testing

#if DEBUG

		public void ValidateData()
		{
			foreach (Player playerOn in Players)
				ValidateData(playerOn);
		}

		private void ValidateData(Player plyr)
		{
			// passengers ok?
			bool error = false;
			if (plyr.PassengersDelivered.Contains(plyr.Passenger))
			{
				if (log.IsWarnEnabled)
					log.Warn(string.Format("{0} is carrying passenger {1} who was already delivered before", plyr.Name,
						plyr.Passenger.Name));
				error = true;
			}
			foreach (Passenger pickupOn in plyr.PickUp.Where(pickupOn => plyr.PassengersDelivered.Contains(pickupOn)))
			{
				if (log.IsWarnEnabled)
					log.Warn(string.Format("{0} wants to pick up passenger {1} who was already delivered before", plyr.Name,
						pickupOn.Name));
				error = true;
			}

			// passenger location ok?
			foreach (Passenger psngrOn in Passengers)
			{
				if (psngrOn.Car != null && psngrOn.Lobby != null)
				{
					if (log.IsWarnEnabled)
						log.Warn(string.Format("Passenger {0} in car {1} and lobby {2}", psngrOn.Name, psngrOn.Car.Location.TilePosition,
							psngrOn.Lobby.Name));
					error = true;
				}
				if (psngrOn.Lobby != null && ! psngrOn.Lobby.Passengers.Contains(psngrOn))
				{
					if (log.IsWarnEnabled)
						log.Warn(string.Format("Passenger {0} in lobby {1} but that company does not list it", psngrOn.Name,
							psngrOn.Lobby.Name));
					error = true;
				}
			}
			foreach (Company cmpyOn in companies)
				foreach (Passenger psngrOn in cmpyOn.Passengers)
					if (psngrOn.Lobby != cmpyOn)
					{
						if (log.IsWarnEnabled)
							log.Warn(string.Format("Passenger {0} in lobby {1} but company {2} lists it", psngrOn.Name,
								psngrOn.Lobby == null ? "null" : psngrOn.Lobby.Name, cmpyOn.Name));
						error = true;
					}

			// path ok?
			if (plyr.Limo.PathTiles.Count > 0)
			{
				Point ptCar = plyr.Limo.Location.TilePosition;
				int x = Math.Abs(plyr.Limo.PathTiles[0].X - ptCar.X);
				int y = Math.Abs(plyr.Limo.PathTiles[0].Y - ptCar.Y);
				if ((x + y != 0) && (x + y != 1))
				{
					ptCar = plyr.Limo.Location.NextTilePosition;
					x = Math.Abs(plyr.Limo.PathTiles[0].X - ptCar.X);
					y = Math.Abs(plyr.Limo.PathTiles[0].Y - ptCar.Y);
					if ((x + y != 0) && (x + y != 1))
					{
						if (log.IsWarnEnabled)
							log.Warn(string.Format("{0} car at {1} has path entry[0] at {2}", plyr.Name, ptCar, plyr.Limo.PathTiles[0]));
						error = true;
					}
				}
			}
			foreach (Point ptTileOn in plyr.Limo.PathTiles)
			{
				MapTile tile = MainMap.Squares[ptTileOn.X][ptTileOn.Y].Tile;
				if (! tile.IsDriveable)
				{
					if (log.IsWarnEnabled)
						log.Warn(string.Format("{0} has path entry[{1}] which is of type {2}", plyr.Name, ptTileOn, tile.Type));
					error = true;
				}
			}
			for (int ind = 1; ind < plyr.Limo.PathTiles.Count; ind++)
			{
				int x = Math.Abs(plyr.Limo.PathTiles[ind].X - plyr.Limo.PathTiles[ind - 1].X);
				int y = Math.Abs(plyr.Limo.PathTiles[ind].Y - plyr.Limo.PathTiles[ind - 1].Y);
				if (x + y != 1)
				{
					if (log.IsWarnEnabled)
						log.Warn(string.Format("{0} at path[{1}] illegal {2} -> {3}", plyr.Name, ind, plyr.Limo.PathTiles[ind - 1],
							plyr.Limo.PathTiles[ind]));
					error = true;
				}
			}

#if STRICT
	// cars on top of each other (never should have 2 in the same quarter tile
			Point ptQtr = BoardLocation.MapToQuarterTile(plyr.Limo.Location.MapPosition);
			foreach (Player plyrOn in Players.Where(plyrOn => plyrOn != plyr).Where(plyrOn => ptQtr == BoardLocation.MapToQuarterTile(plyrOn.Limo.Location.MapPosition)))
			{
				if (log.IsWarnEnabled)
					log.Warn(string.Format("{0} and Player {1} are both on quarter tile {2}", plyr.Name, plyrOn.Name, ptQtr));
				error = true;
			}
#endif

			// car location ok?
			Point pt = plyr.Limo.Location.TilePosition;
			if (! MainMap.Squares[pt.X][pt.Y].Tile.IsDriveable)
			{
				if (log.IsWarnEnabled)
					log.Warn(string.Format("{0} is located at {1} which is of type {2}", plyr.Name, plyr.Limo.Location,
						MainMap.Squares[pt.X][pt.Y].Tile.Type));
				error = true;
			}
			if (plyr.Limo.Location.OffsetTileMoves == 0)
			{
				int xOff = plyr.Limo.Location.MapPosition.X%TileMovement.UNITS_PER_TILE;
				int yOff = plyr.Limo.Location.MapPosition.Y%TileMovement.UNITS_PER_TILE;
				switch (plyr.Limo.Location.Direction)
				{
					case MapSquare.COMPASS_DIRECTION.NORTH:
						if (xOff != 18 || yOff != 0)
						{
							if (log.IsWarnEnabled)
								log.Warn(string.Format("{0} is located at {1} which has a bad offset of {2},{3} (should be 18,0)",
									plyr.Name, plyr.Limo.Location, xOff, yOff));
							error = true;
						}
						break;
					case MapSquare.COMPASS_DIRECTION.EAST:
						if (xOff != 0 || yOff != 18)
						{
							if (log.IsWarnEnabled)
								log.Warn(string.Format("{0} is located at {1} which has a bad offset of {2},{3} (should be 18,0)",
									plyr.Name, plyr.Limo.Location, xOff, yOff));
							error = true;
						}
						break;
					case MapSquare.COMPASS_DIRECTION.SOUTH:
						if (xOff != 6 || yOff != 0)
						{
							if (log.IsWarnEnabled)
								log.Warn(string.Format("{0} is located at {1} which has a bad offset of {2},{3} (should be 18,0)",
									plyr.Name, plyr.Limo.Location, xOff, yOff));
							error = true;
						}
						break;
					case MapSquare.COMPASS_DIRECTION.WEST:
						if (xOff != 0 || yOff != 6)
						{
							if (log.IsWarnEnabled)
								log.Warn(string.Format("{0} is located at {1} which has a bad offset of {2},{3} (should be 18,0)",
									plyr.Name, plyr.Limo.Location, xOff, yOff));
							error = true;
						}
						break;
				}
			}

			Trap.trap(error);
		}

#else
		private void ValidateData()
		{
		}

		private void ValidateData(Player plyr) 
		{
		}
#endif

		#endregion

		private void InfoAndStatus(string msg)
		{
			if (log.IsInfoEnabled)
				log.Info(msg);
			framework.StatusMessage(msg);
		}
	}
}
