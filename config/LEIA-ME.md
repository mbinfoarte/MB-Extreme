# config/canal.json

O MB Extreme lê este arquivo (pelo GitHub, quando o repositório estiver público) para montar a tela **Nossas redes**, o suporte e o aviso de vídeo novo. Mudou aqui, muda nos programas de todo mundo na próxima vez que abrirem — sem lançar versão nova.

- `handle`: o @ do canal (usado no YouTube e nos links padrão).
- `youTubeChannelId`: opcional; o código UC... do canal. Vazio, o programa descobre sozinho pelo @.
- `supportWhatsApp`: número com DDD (ex.: 74999998888). Vazio, o botão do WhatsApp fica escondido.
- `supportHours`: ex.: "Segunda a sexta, 8h às 18h".
- `links`: redes sociais (`icon`: youtube, instagram, tiktok, facebook, threads, whatsapp, telegram, site).
- `offers`: grupos de ofertas, ex.: `{ "name": "WhatsApp Ofertas", "url": "https://chat.whatsapp.com/...", "description": "Promoções todo dia", "icon": "whatsapp" }`.
- `partners`: parceiros com cupom, ex.: `{ "name": "Loja X", "description": "Peças de PC", "coupon": "MB10", "discount": "10% OFF", "url": "https://loja.com/...", "color": "#3DB87A" }`.

Só endereços `https://` são aceitos.
