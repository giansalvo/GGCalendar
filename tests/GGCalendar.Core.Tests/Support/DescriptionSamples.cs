namespace GGCalendar.Core.Tests.Support;

/// <summary>
/// One description written in the editor and the text the card of the event must show (written by hand, not by the parser).
/// <see cref="Look"/> says how it must look, for the person who checks the screenshot.
/// </summary>
public sealed record DescriptionSample(string Name, string Source, string Expected, string Look);

/// <summary>
/// Descriptions with every case of tag, right and wrong. They are checked by the unit tests (the text) and put in a calendar
/// of the app by the semi-automatic test T273, to look at the card (styles, links, lists) in the real window.
/// </summary>
public static class DescriptionSamples
{
    private static readonly string Long = string.Concat(Enumerable.Repeat("lorem ipsum ", 675));

    public static readonly IReadOnlyList<DescriptionSample> All =
    [
        new("01 Stili", "Normale, <b>grassetto</b>, <i>corsivo</i>, <u>sottolineato</u>, <b><i><u>tutti e tre</u></i></b>.",
            "Normale, grassetto, corsivo, sottolineato, tutti e tre.",
            "'grassetto' in grassetto, 'corsivo' in corsivo, 'sottolineato' sottolineato, 'tutti e tre' con i tre stili"),
        new("02 Maiuscole e attributi", "<B>MAIUSCOLO</B> <I>Corsivo</I> <b class=\"x\" id='y'>con attributi</b> <u style=\"color:red\">sottolineato</u>",
            "MAIUSCOLO Corsivo con attributi sottolineato",
            "MAIUSCOLO e 'con attributi' in grassetto, Corsivo in corsivo, sottolineato sottolineato (non rosso)"),
        new("03 A capo", "Riga 1<br>Riga 2<br/>Riga 3<BR />Riga 4\nRiga 5 (a capo nel testo)\r\nRiga 6 (CRLF)",
            "Riga 1\nRiga 2\nRiga 3\nRiga 4\nRiga 5 (a capo nel testo)\nRiga 6 (CRLF)",
            "sei righe, una sotto l'altra, senza righe vuote"),
        new("04 Spazi", "a  b   c\n    rientro di 4 spazi\n\n(riga vuota sopra)",
            "a  b   c\n    rientro di 4 spazi\n\n(riga vuota sopra)",
            "gli spazi doppi e triplici restano, la seconda riga è rientrata, c'è una riga vuota"),
        new("05 Elenco puntato", "Spesa:<ul><li>pane</li><li>latte</li><li>uova</li></ul>Fine",
            "Spesa:\n• pane\n• latte\n• uova\nFine",
            "tre voci con il pallino, 'Fine' a capo dopo l'elenco"),
        new("06 Elenco numerato", "<ol><li>uno</li><li>due</li></ol>Poi un altro elenco:<ol><li>alfa</li><li>beta</li></ol>",
            "1. uno\n2. due\nPoi un altro elenco:\n1. alfa\n2. beta",
            "il secondo elenco riparte da 1"),
        new("07 Elenchi annidati", "<ul><li>A<ul><li>A1</li><li>A2<ul><li>A2a</li></ul></li></ul></li><li>B</li></ul>",
            "• A\n  • A1\n  • A2\n    • A2a\n• B",
            "tre livelli, ognuno rientrato di più"),
        new("08 Elenco su più righe", "Elenco:\n<ul>\n  <li>uno</li>\n  <li>due</li>\n</ul>\nFine",
            "Elenco:\n• uno\n• due\nFine",
            "nessuna riga vuota tra le voci"),
        new("09 Voci senza elenco", "<li>voce fuori da un elenco</li><li>seconda voce</li>",
            "• voce fuori da un elenco\n• seconda voce",
            "due voci con il pallino"),
        new("10 Stili e link in un elenco", "<ul><li><b>grassetto</b> e <i>corsivo</i></li><li><a href=\"https://example.org\">link</a></li></ul>",
            "• grassetto e corsivo\n• link",
            "'grassetto' in grassetto, 'corsivo' in corsivo, 'link' è un link blu sottolineato"),
        new("11 Link", "<a href=\"https://example.org/pagina?x=1&amp;y=2\">link http</a>, <a href=\"mailto:info@example.org\">link mail</a>, <A HREF='http://example.org'>maiuscolo</A>",
            "link http, link mail, maiuscolo",
            "tre link blu sottolineati; il clic chiede conferma e mostra l'indirizzo (con '&' al posto di '&amp;')"),
        new("12 Indirizzi nudi", "Sito: https://example.org/doc, www.example.org, mail: mario@example.org. Non link: esempio.it e file.txt",
            "Sito: https://example.org/doc, www.example.org, mail: mario@example.org. Non link: esempio.it e file.txt",
            "link solo i primi tre indirizzi (senza la virgola e il punto finali); esempio.it e file.txt sono testo normale"),
        new("13 Link vietati", "<a href=\"javascript:alert(1)\">js</a> <a href=\"file:///c:/x\">file</a> <a name=\"x\">senza href</a> <a href=\"\">vuoto</a>",
            "<a href=\"javascript:alert(1)\">js</a> <a href=\"file:///c:/x\">file</a> <a name=\"x\">senza href</a> <a href=\"\">vuoto</a>",
            "tutto il testo con i tag visibili, nessun link blu"),
        new("14 Tag p strong em div span h1", "<p>paragrafo</p> <strong>strong</strong> <em>em</em> <div class=\"x\">div</div> <span>span</span> <h1>titolo</h1>",
            "paragrafo\n\nstrong em\ndiv\nspan\ntitolo",
            "'paragrafo', una riga vuota, 'strong' in grassetto e 'em' in corsivo sulla stessa riga, 'div' e 'span' ognuno su una riga, 'titolo' grande e in grassetto"),
        new("15 Pagina HTML", "<!DOCTYPE html>\n<html>\n<head><title>T</title></head>\n<body>\n<p>Ciao <b>mondo</b></p>\n</body>\n</html>",
            "Ciao mondo",
            "solo 'Ciao mondo' con 'mondo' in grassetto: niente DOCTYPE, html, head, title, body né righe vuote"),
        new("16 Entità", "&amp; &lt;b&gt; &quot;x&quot; &copy; &euro; &agrave; &hellip; &#8217; &#x1F600; &nbsp;|&nbsp; &unknown; &apos; &amp",
            "& <b> \"x\" © € à … ’ \U0001F600 \u00A0|\u00A0 &unknown; ' &amp",
            "i caratteri & < > \" © € à … ’ ' e una faccina; restano com'è scritto: &unknown; e &amp senza punto e virgola; '<b>' è testo, non grassetto"),
        new("17 Tag errati", "<b>grassetto mai chiuso <i>corsivo</i> ancora grassetto fino alla fine\nchiusure orfane: </u></i></a></ul>|\nTag rotto: <b class=\"x e <i>dopo</i>",
            "grassetto mai chiuso corsivo ancora grassetto fino alla fine\nchiusure orfane: |\nTag rotto: <b class=\"x e dopo",
            "tutto in grassetto fino alla fine; 'corsivo' e 'dopo' anche in corsivo; le chiusure orfane non si vedono; '<b class=\"x e ' è testo"),
        new("18 Tag dentro un tag", "<img alt=\"<b>non grassetto</b>\" src=\"https://example.org/i.png\"> e <!-- <i>commento</i> --> fine",
            "<img alt=\"<b>non grassetto</b>\" src=\"https://example.org/i.png\"> e  fine",
            "il tag img resta come testo, senza stile; il commento sparisce (restano due spazi tra 'e' e 'fine'); solo l'indirizzo https://example.org/i.png è un link"),
        new("19 Spazi negli elenchi", "<ul> <li> uno </li> <li>\tdue\t</li> </ul>", "• uno\n• due",
            "due voci con il pallino, senza spazi prima o dopo il testo"),
        new("20 Paragrafi", "Prima<p>Secondo paragrafo</p>Dopo<p>Terzo</p>",
            "Prima\n\nSecondo paragrafo\n\nDopo\n\nTerzo",
            "quattro blocchi separati da una riga vuota"),
        new("21 Titoli", "<h1>Titolo 1</h1><h2>Titolo 2</h2><h3>Titolo 3</h3>testo normale",
            "Titolo 1\nTitolo 2\nTitolo 3\ntesto normale",
            "tre titoli in grassetto, il primo più grande del secondo e il secondo del terzo; il testo normale è più piccolo"),
        new("22 Commenti e DOCTYPE", "<!DOCTYPE html>prima <!-- nascosto <b>x</b> --> dopo",
            "prima  dopo",
            "solo 'prima  dopo' (due spazi): il DOCTYPE e il commento, con il suo contenuto, non si vedono"),
        new("23 Tag non letti", "<h4>titolo 4</h4> <table><tr><td>cella</td></tr></table> <my-tag>x</my-tag>",
            "<h4>titolo 4</h4> <table><tr><td>cella</td></tr></table> <my-tag>x</my-tag>",
            "tutto il testo con i tag visibili, nessuno stile"),
        new("24 Descrizione lunga", Long + "<b>fine</b>", Long + "fine",
            "al massimo 8 righe, con i puntini alla fine; 'fine' non si vede nella scheda (nell'editor sì)"),
    ];
}
