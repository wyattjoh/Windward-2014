from framework import playerPowerSend
from api.units import CARD as POWER_UPS

class powerUpManager(object):
    def __init__(self, brain, powerUpDeck):
        self.brain = brain
        self.hand = []
        self.deck = powerUpDeck
        self.deckDict = dict(zip(POWER_UPS, ([] for i in range(len(POWER_UPS)))))
        self.mult_passengers = []
        self.mult_companies = []

        print self.deckDict

        for card in powerUpDeck:
            self.deckDict[card.card].append(card)
        for card in self.deckDict['MULT_DELIVERING_PASSENGER']:
            self.mult_passengers.append(card.passenger)
        for card in self.deckDict['MULT_DELIVER_AT_COMPANY']:
            self.mult_companies.append(card.company)

    def playPowerUp(self, powerUp, passenger=None, player=None, company=None):
        ''' 
        Play the specified power up. If there are no cards, do nothing.
        '''
        powers = [card for card in self.hand if card.card == powerUp]
        if not powers:
            return

        if powerUp == 'MULT_DELIVERING_PASSENGER':
            card = None
            for power in powers:
                if power.passenger == passenger:
                    card = power
                    break
            if not card:
                return
            self.playCard(card)
            return
        elif powerUp == 'MULT_DELIVER_AT_COMPANY':
            card = None
            for power in powers:
                if power.company == company:
                    card = power
                    break
            if not card:
                return
            self.playCard(card)
            return

        card = powers[0]
        if powerUp == 'MOVE_PASSENGER':
            card.passenger = passenger
            self.playCard(card)
        elif powerUp == 'CHANGE_DESTINATION' or powerUp == 'STOP_CAR':
            card.player = player
            self.playCard(card)
        else:
            self.playCard(card)

    def drawPowerUp(self, powerUp, passenger=None, company=None):
        '''
        Attempt to draw the specified power up. If it is not in the deck,
        returns False, else True.
        '''
        powers = self.deckDict[powerUp]
        if not powers:
            return False

        if powerUp == 'MULT_DELIVERING_PASSENGER':
            card = None
            for power in powers:
                if power.passenger == passenger:
                    card = power
                    break
            if not card:
                return False
            self.drawCard(card)
            return True
        elif powerUp == 'MULT_DELIVER_AT_COMPANY':
            card = None
            for power in powers:
                if power.company == company:
                    card = power
                    break
            if not card:
                return False
            self.drawCard(card)
            return True

        card = powers[0]
        self.drawCard(card)
        return True

    def playCard(self, card):
        '''
        Play the specified card and remove it from our deck.
        '''
        playerPowerSend(self.brain, "PLAY", card)
        self.removeCardFromHand(card)

    def discardCard(self, card):
        '''
        Discard the specified card and remove it from our deck.
        '''
        playerPowerSend(self.brain, "DISCARD", card)
        self.removeCardFromHand(card)

    def drawCard(self, card):
        '''
        Draw the specified card from our deck.
        '''
        print 'Drawing card: ', card.card
        playerPowerSend(self.brain, "DRAW", card)
        self.removeCardFromDeck(card)

    def removeCardFromHand(self, card):
        self.hand.remove(card)

    def removeCardFromDeck(self, card):
        '''
        Remove the specified card from our deck.
        '''
        self.deckDict[card.card].remove(card)
        self.deck.remove(card)
        if card.card == 'MULT_DELIVERING_PASSENGER':
            self.mult_passengers.remove(card.passenger)
        elif card.card == 'MULT_DELIVER_AT_COMPANY':
            self.mult_companies.remove(card.company)

    def cardIsInDeck(self, powerUp, passenger=None, company=None):
        powers = self.deckDict[powerUp]
        if not powers:
            return False

        if powerUp == 'MULT_DELIVERING_PASSENGER':
             for power in powers:
                if power.passenger == passenger:
                    return True
             return False
        elif powerUp == 'MULT_DELIVER_AT_COMPANY':
            for power in powers:
                if power.company == company:
                    return True
            return False
        else:
            return True

    def cardIsInHand(self, powerUp, passenger=None, company=None):
        for card in self.hand:
            if card.card == powerUp:
                if powerUp == 'MULT_DELIVERING_PASSENGER':
                    if card.passenger == passenger:
                        return True
                elif powerUp == 'MULT_DELIVER_AT_COMPANY':
                    if card.company == company:
                        return True
                else:
                    return True
        return False

    def cardHasBeenPlayed(self, powerUp, passenger=None, company=None):
        return not (self.cardIsInHand(powerUp, passenger, company) or
                    self.cardIsInDeck(powerUp, passenger, company))
