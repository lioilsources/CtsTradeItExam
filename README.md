# CtsTradeItExam

## Zadání
Efektivně načíst velký XML soubor a uložit jej efektivně do DB. 
K vytvoření XML souboru a simulaci SQL transakčního prostředí využít dodanou DLL knihovnu.

### Originální zadání
Cílem je vytvořit aplikaci, která bude načítat obchody z dodaného XML souboru a simulovaně je ukládat do systému. Tento úkol slouží pro posouzení uchazeče o pozici CTS vývojáře.
Primárně je nutné se zaměřit na efektivitu navrženého řešení a současně na kvalitu kódu z hlediska jeho přehlednosti a srozumitelnosti. Je nutné řešit problematiku načtení velkého XML souboru. Dále je nutné řešit simulaci transakčního přístupu do databáze.
Pro vytvoření velkého XML souboru použijte metodu CreateTestFile(string path, int count) ve třídě Tester v dodané knihovně. Path udává cestu do adresáře, kde bude soubor TradesList.xml vygenerován. Jako count použijte hodnotu alespoň 1 000 000. Takto vygenerovaný soubor bude mít více než 100 MB.
V případě dotazů nás samozřejmě můžete kontaktovat.

### Bonusový úkol
Jako bonusový úkol je možné zjistit pro každý cenný papír (tedy pro každý ISIN) sumy množství deseti nejlepších nákupů a deseti nejlepších prodejů. U nákupu platí, že čím nižší cena, tím lepší nákup. U prodejů platí čím vyšší cena, tím lepší prodej.

## Řešení
Použity dvě varianty parsování XML souboru.
1) Custom: API parsing + ruční deserializace
2) XmlSerializer API + anotace custom tříd

Z důvodu lepší výkonnocti Custom řešení, bylo elegantnější řešení pomocí API zakomentováno.

Myšlenka efektivního transakčního ukládání obchodů do databáze byla seskupit operaci INSERT do větších celků a 
posílání těchto celků do databáze v jednotlivých transakcích. 
Dále byl implementován mechanismus opětovného zaslání chybné transakce.
Vzhledem k velké chybovosti testovací databáze (10% chybovost operací) se nevyplatí seskupovat obchody ve skupinách větších
než 9.

Myšlenka hledání optimálního nastavení procesu importu vychází z předpokladu, že se snažíme, aby všechny transakce proběhly 
úspěšně (počet znovuopakování je vyšší), počet transakcí byl co nejmenší (skupiny co největší) a počet znovuopakování 
co nejmenší.

Experimentem bylo objeveno "optimální" nastavení parametrů procesu: skupiny po 9, počet opakování znovuzaslání 
chybné transakce 10. 

Je možné, že existuje lepší nastavení. 

## Bonusový úkol
Byl implementován bonusový úkol.

## Výstup aplikace při počtu 1.000.000 obchodů
Looking for source files in /Users/oldrichvorechovskyjr/Documents/Devel/csharp/CtsTrade/CtsTrades/Data.\
Creating test file of 1000000\
... elapsed 00:00:11.9616765\
Reading file\
... elapsed 00:00:06.4093016\
Parsing XML + Custom deserializing\
... elapsed 00:00:30.0996779\
PROCESS DATABASE\
Transactions/No of retries/Operations: 111111/49054/160165\
... elapsed 00:12:29.8160450\
Best BUYS / from lower\
BMG200452024: 666.00/41546\
CZ0008040318: 774.02/41859\
NL0009604859: 900.02/41515\
... elapsed 00:00:05.4644752\
Best SELS / from higher\
CS0008418869: 147265.16/41099\
CZ0008019106: 10482.80/42186\
LU0275164910: 9129.75/41748\
... elapsed 00:00:03.9727422\

## Struktura aplikace
Program.cs - řešení úkolu\
/Data - XML s vygenerovanými obchody\
/Library - testovací knihovna\
/Output - LOG simulovaného ukládání do DB
