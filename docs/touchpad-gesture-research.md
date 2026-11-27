# Дослідження: три пальці на Precision Touchpad

Issue: [#85 — Тачпад: клік трьома пальцями як середня кнопка](https://github.com/greenyarik0505-jpg/windows-tools/issues/85)

## Висновок

Вказаний в issue шлях через Raw Input для Precision Touchpad не підходить: Windows відкриває
колекцію Precision Touchpad ексклюзивно для Raw Input Manager. Windows має окремий API
`TouchpadGesturesController` для жестів трьома й більше пальцями, але застосунок отримує їх, лише
коли він на передньому плані. Тому підтримати глобальне переназначення для будь-якої програми
з доступних API не можна.

Обмежений варіант технічно можливий лише для вікна WindowsTools на передньому плані та за умови,
що користувач вимкнув обробку трьох- і чотирипальцевих жестів системою в налаштуваннях Windows.
У цьому режимі застосунок може отримати `ThreeFingerTap` через `TouchpadGesturesController` і
надіслати середній клік у власний контекст. Увімкнення цього режиму відбирає ці ж жести у Windows,
зокрема системні дії Task View, перемикання вікон або віртуальних робочих столів.

## Сумісність із системними жестами

Windows 11 має перемикач **Bluetooth & devices → Touch → Three- and four-finger touch gestures**:

- **On** (типове значення): жести обробляє Windows, застосунок не отримує три- й чотирипальцеві
  взаємодії.
- **Off**: взаємодії доступні застосунку, але системні жести для них вимкнені.

Отже, глобальний жест не може одночасно залишитися системним і бути перехопленим WindowsTools.
Модуль має лишати перемикач під контролем користувача, не змінювати його самостійно й пояснювати
наслідки перед переходом до налаштувань Windows.

## Пристрої без Precision Touchpad

`TouchpadGesturesController` призначений для Precision Touchpad. Raw Input не є запасним способом
читати контакти: збірка Windows відкриває PTP HID-колекцію ексклюзивно для власного Raw Input
Manager. Стандартні тачпади можуть надсилати мишачі події через драйвер, але таку подію не можна
надійно відрізнити від фізичної миші й визначити, що її створили саме три пальці. Тому модуль не
повинен вгадувати пристрій за звичайною подією середньої кнопки.

## Рекомендована зміна обсягу

Якщо залишати фічу в плані, обмежити її так:

1. Працює лише у вікні WindowsTools, коли воно активне.
2. Вимагає сумісного Precision Touchpad та вимкнених у Windows системних три- й чотирипальцевих
   жестів.
3. Показує користувачу пояснення й посилання `ms-settings:devices-touch`; не змінює налаштування
   за нього.
4. Перетворює тільки `ThreeFingerTap` на середній клік у межах активного вікна.
5. Якщо системні жести увімкнені або контролер недоступний, модуль вимкнений або показує причину.

Це вже інша поведінка, ніж глобальний клік у будь-якому застосунку. Рекомендація: не реалізовувати
початковий глобальний варіант; спершу звузити issue до явного режиму для активного вікна.

## Джерела

- [Precision Touchpad Input](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/precision-touchpad-portal): Windows обробляє контакти PTP; `TouchpadGesturesController` отримує жести трьома й більше пальцями, коли застосунок у передньому плані.
- [HID Architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-architecture): колекція Precision Touchpad (Usage Page `0x0D`, Usage `0x05`) ексклюзивна; Raw Input Manager відкриває такі пристрої ексклюзивно.
- [TouchpadGesturesController](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/touchpadgesturescontroller): окремо описує `ThreeFingerTap`, трипальцевий натиск і системні дії.
- [Three-finger and four-finger touch interactions don’t work in Windows apps](https://learn.microsoft.com/en-us/troubleshoot/windows/win32/finger3or4-touch-interaction-no-longer-works): при `On` жести обробляє система, при `Off` — застосунки, але не система.
