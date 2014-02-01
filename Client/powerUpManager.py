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
