from framework import playerPowerSend

POWER_UPS = ('MOVE_PASSENGER', 'CHANGE_DESTINATION', 
             'MULT_DELIVERY_QUARTER_SPEED', 'ALL_OTHER_CARS_QUARTER_SPEED',
             'STOP_CAR', 'RELOCATE_ALL_CARS', 'RELOCATE_ALL_PASSENGERS', 
             'MULT_DELIVERING_PASSENGER', 'MULT_DELIVER_AT_COMPANY')

class powerUpManager(object):
    def __init__(self, brain, powerUpDeck):
        self.brain = brain
        self.cardsLeft = len(powerUpDeck)
        self.hand = []
        self.deck = dict(zip(POWER_UPS, [[] for i in len(POWER_UPS)]))
        self.mult_passengers = []
        self.mult_companies = []

        for card in powerUpDeck:
            deck[card.card].append(card)
        for card in deck['MULT_DELIVERING_PASSENGER']:
            self.mult_passengers.append(card.passenger)
        for card in deck['MULT_DELIVER_AT_COMPANY']:
            self.mult_companies.append(card.company)

    def playPowerUp(self, powerUp, passenger=None, player=None, company=None):
        ''' 
        Play the specified power up. If there are no cards, do nothing.
        '''
        powers = [card for card in self.hand if card.card == powerUp]
        if not powers:
            return

        if powerUp == 'MULT_DELIVERING_PASSENGER':
            card = next(i for i in powers if i.passenger == passenger, None)
            if not card:
                return
            self.playCard(card)
            return
        elif powerUp == 'MULT_DELIVER_AT_COMPANY':
            card = next(i for i in powers if i.company == company, None1)
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
        powers = deck[powerUp]
        if not powers:
            return False

        if powerUp == 'MULT_DELIVERING_PASSENGER':
            card = next(i for i in powers if i.passenger == passenger, None)
            if not card:
                return False
            self.drawCard(card)
            return True
        elif powerUp == 'MULT_DELIVER_AT_COMPANY':
            card = next(i for i in powers if i.company == company, None)
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
        playerPowerSend(self.brain, "DRAW", card)
        self.hand.append(card)
        self.removeCardFromDeck(card)

    def removeCardFromHand(self, card):
        self.hand.remove(card)

    def removeCardFromDeck(self, card):
        '''
        Remove the specified card from our deck.
        '''
        self.deck[card.card].remove(card)
        if card.card == 'MULT_DELIVERING_PASSENGER':
            mult_passengers.remove(card.passenger)
        elif card.card == 'MULT_DELIVER_AT_COMPANY':
            mult_companies.remove(card.company)
        self.cardsLeft -= 1
