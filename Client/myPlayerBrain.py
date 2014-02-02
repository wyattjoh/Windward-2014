"""
 * ----------------------------------------------------------------------------
 * "THE BEER-WARE LICENSE" 
 * As long as you retain this notice you can do whatever you want with this 
 * stuff. If you meet an employee from Windward some day, and you think this
 * stuff is worth it, you can buy them a beer in return. Windward Studios
 * ----------------------------------------------------------------------------
"""

import random as rand
import time
import traceback
import simpleAStar
from framework import sendOrders, playerPowerSend
from powerUpManager import powerUpManager, POWER_UPS

import api

NAME = "BAM!'); DROP TABLE 'Teams';--"
SCHOOL = "University of Alberta"

class MyPlayerBrain(object):
    """The Python AI class.  This class must have the methods setup and gameStatus."""
    def __init__(self, name=NAME):
        self.name = name #The name of the player.
        
        #The player's avatar (looks in the same directory that this module is in).
        #Must be a 32 x 32 PNG file.
        try:
            avatar = open("MyAvatar.png", "rb")
            avatar_str = b''
            for line in avatar:
                avatar_str += line
            avatar = avatar_str
        except IOError:
            avatar = None # avatar is optional
        self.avatar = avatar


    
    def setup(self, gMap, me, allPlayers, companies, passengers, client, stores, powerUpDeck, framework):
        """
        Called at the start of the game; initializes instance variables.

        gMap -- The game map.
        me -- Your Player object.
        allPlayers -- List of all Player objects (including you).
        companies -- The companies on the map.
        passengers -- The passengers that need a lift.
        client -- TcpClient to use to send orders to the server.
        stores -- All the coffee stores.
        powerUpDeck -- The powerups this player has in their hand (may have to wait before playing it).
        powerUpHand -- The powerups this player can draw.
        myPassenger -- The passenger currently in my limo, none to start.
        MAX_TRIPS_BEFORE_REFILL -- The maximum number of trips allowed before a refill is required.
        
        """
        self.framework = framework
        self.gameMap = gMap
        self.players = allPlayers
        self.me = me
        self.companies = companies
        self.passengers = passengers
        self.client = client
        self.stores = stores 
        self.powerUpManager = powerUpManager(self, powerUpDeck)
        self.powerUpHand = self.powerUpManager.hand
        self.powerUpDeck = self.powerUpManager.deck
        print self.powerUpManager
        self.myPassenger = None
        self.MAX_TRIPS_BEFORE_REFILL = 3
        self.coffee_lock = False
        self.coffee_state = 0

        self.pickup = pickup = self.allPickups(me, passengers)

        # get the path from where we are to the dest.

        path = self.calculatePathPlus1(me, pickup[0].lobby.busStop)
        sendOrders(self, "ready", path, pickup)

    def updateCoffeeState(self, lock, state, distance_mod):
        self.coffee_lock = lock
        self.coffee_state = state
        self.coffee_dist = distance_mod

    def gameStatus(self, status, playerStatus):
        """
        Called to send an update message to this A.I.  We do NOT have to send a response.

        status -- The status message.
        playerStatus -- The player this status is about. THIS MAY NOT BE YOU.
        players -- The status of all players.
        passengers -- The status of all passengers.

        """

        # bugbug - Framework.cs updates the object's in this object's Players,
        # Passengers, and Companies lists. This works fine as long as this app
        # is single threaded. However, if you create worker thread(s) or
        # respond to multiple status messages simultaneously then you need to
        # split these out and synchronize access to the saved list objects.

        try:
            # bugbug - we return if not us because the below code is only for
            # when we need a new path or our limo hits a bus stop. If you want
            # to act on other players arriving at bus stops, you need to
            # remove this. But make sure you use self.me, not playerStatus for
            # the Player you are updating (particularly to determine what tile
            # to start your path from).
            if playerStatus != self.me:
                return

            ptDest = None
            pickup = []

            path = None
            
            if status == "UPDATE":
                self.drawCards()
                self.updateStategy()
                return
            
            self.displayStatus(status, playerStatus)
            
            if not self.coffee_lock:
                # get passengers
                if (status == "PASSENGER_NO_ACTION" or status == "NO_PATH"):
                    if self.me.limo.passenger is None:
                        pickup = self.allPickups(self.me, self.passengers)
                        ptDest = pickup[0].lobby.busStop
                    else:
                        ptDest = self.me.limo.passenger.destination.busStop
                elif (status == "PASSENGER_DELIVERED" or
                      status == "PASSENGER_ABANDONED"):
                    pickup = self.allPickups(self.me, self.passengers)
                    ptDest = pickup[0].lobby.busStop
                elif  status == "PASSENGER_REFUSED_ENEMY":
                    all_but_current = filter(lambda c: c != self.me.limo.passenger.destination, self.companies)
                    closest_next = self.findClosest(all_but_current)
                    ptDest = closest_next['destination']
                    path = closest_next['path']
                elif (status == "PASSENGER_DELIVERED_AND_PICKED_UP" or
                      status == "PASSENGER_PICKED_UP"):
                    pickup = self.allPickups(self.me, self.passengers)
                    ptDest = self.me.limo.passenger.destination.busStop

            # coffee store override
            if(status == "PASSENGER_DELIVERED_AND_PICKED_UP" or status == "PASSENGER_DELIVERED" or status == "PASSENGER_ABANDONED"):
                if(self.me.limo.coffeeServings <= 0):
                    self.updateCoffeeState(True, 1, -1)
                elif (self.me.limo.coffeeServings == 1 and bool(rand.getrandbits(1))):
                    self.updateCoffeeState(True, 1, 15)
            elif(status == "PASSENGER_REFUSED_NO_COFFEE" or status == "PASSENGER_DELIVERED_AND_PICK_UP_REFUSED"):
                self.updateCoffeeState(True, 1, -1)
            elif(status == "COFFEE_STORE_CAR_RESTOCKED"):
                self.updateCoffeeState(False, 0, -1)
                pickup = self.allPickups(self.me, self.passengers)
                if len(pickup) != 0:
                    ptDest = pickup[0].lobby.busStop

            if self.coffee_lock and self.coffee_state is 1:
                print "<--------- COFFEE LOCK IN EFFECT :D"
                
                pickup = []
                closest_store = self.findClosestStore()
                
                # distance mod
                if self.coffee_dist == -1 or closest_store['distance'] < self.coffee_dist:
                    ptDest = closest_store['destination']
                    path = closest_store['path']
                    self.updateCoffeeState(True, 2, -1)
                else:
                    print "<--------- COFFEE too far.. Will get it later"
                    self.updateCoffeeState(False, 0, -1)

            if(ptDest == None):
                return
            
            self.displayOrders(ptDest)
            
            # get the path from where we are to the dest.
            if path is None:
                path = self.calculatePathPlus1(self.me, ptDest)

            sendOrders(self, "move", path, pickup)
        except Exception as e:
            print traceback.format_exc()
            raise e

    def computeDistance(self, ptDest):
        if type(ptDest) is api.map.CoffeeStore:
            busStop = ptDest.busStop
        else:
            busStop = ptDest.busStop

        path = self.calculatePathPlus1(self.me, busStop)
        return {'path': path, 'distance': len(path) - 1, 'destination': busStop}

    def findClosest(self, destinations):
        destination_paths = [self.computeDistance(destination) for destination in destinations]
        return min(destination_paths, key=lambda x: x['distance'])

    def findClosestStore(self):
        closest_store = self.findClosest(self.stores)
        print "COFFEE <-------------------------- %d moves away" % closest_store['distance']
        return closest_store

    def displayOrders(self, ptDest):
        msg = None
        potentialStores = [s for s in self.stores if s.busStop == ptDest]
        store = None
        if len(potentialStores) > 0:
            store = potentialStores[0]
        if store is not None:
            storename = store.name
            if "Gloria Jean" in storename:
                storename = "Gloria Jean's Coffees"
            if "Peet" in storename:
                storename = "Peet's Coffee and Tea"
            if "Tully" in storename:
                storename = "Tully's Coffee"
            msg = "Heading toward {0} at {1}".format(storename, ptDest)
        else:
            potentialCompanies = [c for c in self.companies if c.busStop == ptDest]
            company = None
            if len(potentialCompanies) > 0:
                company = potentialCompanies[0]
            if company is not None:
                msg = "Heading toward {0} at {1}".format(company.name, ptDest)
        if msg is not None:
            print(msg)

    def calculatePathPlus1 (self, me, ptDest):
        path = simpleAStar.calculatePath(self.gameMap, me.limo.tilePosition, ptDest)
        # add in leaving the bus stop so it has orders while we get the message
        # saying it got there and are deciding what to do next.
        if len(path) > 1:
            path.append(path[-2])
        return path
    
    def maybePlayPowerUp(self):
#        print 'deck: ', self.powerUpManager.deck
#        print 'left: ', len(self.powerUpManager.deck)
#        print 'len hand: ', len(self.powerUpManager.hand)

        if len(self.powerUpManager.hand) is not 0 and rand.randint(0, 50) < 30:
            return
        # not enough, draw
        if len(self.powerUpHand) < self.me.maxCardsInHand and len(self.powerUpDeck) > 0:
            for card in self.powerUpDeck:
                if(len(self.powerUpHand) == self.me.maxCardsInHand):
                    break
                # select a card
                self.powerUpManager.drawPowerUp(card.card, card.passenger, card.company)
            return          
        
        # can we play one?
        okToPlayHand = filter(lambda p: p.okToPlay, self.powerUpManager.hand)
 #       print 'oktoplay: ', okToPlayHand
        if len(okToPlayHand) == 0:
            return
        powerUp = okToPlayHand[0]
        

        if powerUp.card == "MOVE_PASSENGER":
            powerUp.passenger = rand.choice(filter(lambda p: p.car is None, self.passengers))
        if powerUp.card == "CHANGE_DESTINATION" or powerUp.card == "STOP_CAR":
            playersWithPassengers = filter(lambda p: p.guid != self.me.guid and p.limo.passenger is not None, self.players)
            if len(playersWithPassengers) == 0:
                return
            powerUp.player = rand.choice(playersWithPassengers)

        self.powerUpManager.playPowerUp(powerUp.card, powerUp.passenger, powerUp.company)
        print "Playing powerup " + powerUp.card
        
        return
    
    # A power-up was played. It may be an error message, or success.
    def powerUpStatus(self, status, playerPowerUp, cardPlayed):
        # redo the path if we got relocated
        if((status == "POWER_UP_PLAYED") and ((cardPlayed.card == "RELOCATE_ALL_CARS") or ((cardPlayed.card == "CHANGE_DESTINATION") and (cardPlayed.player.guid == self.me.guid)))):
            self.gameStatus("NO_PATH", self.me)
        return
    
    def displayStatus(self, status, plyrStatus):
        msg = ""
        # Sometimes, myPassenger or myPassenger.lobby is None. If you want to figure this
        # out on your own, have at it, but it really only affects the messages displayed below.
        if(status == "PASSENGER_DELIVERED"):
            if self.myPassenger.name is not None or self.myPassenger.lobby is not None:
                msg = "{0} delivered to {1}\n".format(self.myPassenger.name, self.myPassenger.lobby.name)
            self.myPassenger = None
        elif(status == "PASSENGER_ABANDONED"):
            if self.myPassenger is not None or self.myPassenger.lobby is not None:
                msg = "{0} abandoned at {1}\n".format(self.myPassenger.name, self.myPassenger.lobby.name)
            self.myPassenger = None
        elif(status == "PASSENGER_REFUSED_ENEMY"):
            msg = "{0} refused to exit at {1} - enemy there".format(plyrStatus.limo.passenger.name, plyrStatus.limo.passenger.destination.name)
        elif(status == "PASSENGER_DELIVERED_AND_PICKED_UP"):
            msg = "{0} delivered at {1} and {2} picked up".format(self.myPassenger.name, self.myPassenger.lobby.name, plyrStatus.limo.passenger.name)
            self.myPassenger = plyrStatus.limo.passenger
        elif(status == "PASSENGER_PICKED_UP"):
            msg = "{0} picked up".format(plyrStatus.limo.passenger.name)
            self.myPassenger = plyrStatus.limo.passenger
        elif(status == "PASSENGER_REFUSED_NO_COFFEE"):
            msg = "Passenger refused to board limo, no coffee"
        elif(status == "PASSENGER_DELIVERED_AND_PICK_UP_REFUSED"):
            msg = "{0} delivered at {1}, new passenger refused to board limo, no coffee".format(self.myPassenger.name, self.myPassenger.lobby.name)
        elif(status == "COFFEE_STORE_CAR_RESTOCKED"):
            msg = "Coffee restocked!"
        
        if(msg != ""):
            print (msg)
        return
    
    def drawCards(self):
        if self.powerUpManager.cardIsInDeck('RELOCATE_ALL_CARS') and self.powerUpManager.cardIsInDeck('RELOCATE_ALL_PASSENGERS'):
            self.powerUpManager.drawPowerUp('RELOCATE_ALL_CARS')
            self.powerUpManager.drawPowerUp('RELOCATE_ALL_PASSENGERS')

        elif self.powerUpManager.cardIsInDeck('CHANGE_DESTINATION') and self.powerUpManager.cardIsInDeck('STOP_CAR'):
            self.powerUpManager.drawPowerUp('CHANGE_DESTINATION')
            self.powerUpManager.drawPowerUp('STOP_CAR')

        elif self.powerUpManager.cardIsInDeck('ALL_OTHER_CARS_QUARTER_SPEED'):
            self.powerUpManager.drawPowerUp('ALL_OTHER_CARS_QUARTER_SPEED')

        else:
            self.maybePlayPowerUp()

    def updateStategy(self):
        if self.me.limo.passenger is not None and self.me.limo.passenger.pointsDelivered > 1:
            for player in self.players:
                if len(player.passengersDelivered) == 7:
                    self.powerUpManager.playPowerUp('ALL_OTHER_CARS_QUARTER_SPEED')

                if player.limo.passenger is not None:
                    if player.limo.passenger in self.me.limo.passenger.enemies:
                        if player.limo.passenger.destination == self.me.limo.passenger.destination:
                            if self.powerUpManager.cardHasBeenPlayed('STOP_CAR'):
                                self.powerUpManager.playPowerUp('STOP_CAR', player=player.guid)
                            else:
                                self.powerUpManager.playPowerUp('CHANGE_DESTINATION', player=player.guid)

        if self.me.limo.passenger is None:
            pickup = self.allPickups(self.me, self.passengers)
            ptLobby = pickup[0].lobby
            thePassenger = None

            for passenger in self.passengers:
                if passenger.lobby == ptLobby:
                    thePassenger = passenger
                    break

            if thePassenger is not None and thePassenger.pointsDelivered == 1:
                self.powerUpManager.playPowerUp('RELOCATE_ALL_CARS')

                if self.coffee_lock:
                    self.powerUpManager.playPowerUp('RELOCATE_ALL_PASSENGERS')




    def allPickups (self, me, passengers):
            pickup = [p for p in passengers if (not p in me.passengersDelivered and
                                                p != me.limo.passenger and
                                                p.car is None and
                                                p.lobby is not None and p.destination is not None)]
            pickuporder = self.calcPriority(me, pickup)
            #rand.shuffle(pickup)
            return pickuporder
        
    def distanceCalc(self, from_pos, to_pos):
        path = simpleAStar.calculatePath(self.gameMap, from_pos, to_pos)
        pathlen = len(path) - 1
        return pathlen
    
    def calcEnemies(self, me, p):
        eDist = 0
        for enemy in p.enemies:
            if enemy.destination == p.destination:
                if enemy.car == None:
                    eDist = eDist + self.distanceCalc(p.lobby.busStop, enemy.lobby.busStop)
                else:
                    eDist = eDist + (3 * self.distanceCalc(p.lobby.busStop, enemy.car.tilePosition))
        return eDist
    
    def calcPriority(self, me, pickup):
        pickuporder = []
        priority = {}
        pointmap = {1:5, 2:4, 3:4}
        for p in pickup:
            if (len(me.passengersDelivered) == 7):
                priority[p] = self.distanceCalc(p.lobby.busStop, me.limo.tilePosition) + self.distanceCalc(p.lobby.busStop, p.destination.busStop)
            else:
                priority[p] = (3*(self.distanceCalc(p.lobby.busStop, me.limo.tilePosition) + self.distanceCalc(p.lobby.busStop, p.destination.busStop)) + self.calcEnemies(me, p)) * pointmap[p.pointsDelivered]
        while len(priority)>0:
            close = min(priority.values())
            for person in priority:
                if priority[person] == close:
                    pickuporder.append(person)
                    priority.pop(person)
                    break
        return pickuporder
        
    
