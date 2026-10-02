The Settings control for choosing the highlight colour: five round swatches with names underneath.

- **Swatches:** 40px circles (`radius-20`) filled `Preset-Green`, `Preset-Blue`, `Preset-Red` and `Preset-Cognac` for the current theme, plus "Windows accent" drawn as a `Bg-Raised` circle with a `Border-Strong` edge and a four-pane icon. Names sit 6px below in 12px `Text-Secondary`; each column is 64px, 18px apart.
- **Chosen:** a white tick in the swatch and a 2px `Text-Primary` ring outside a 2px `Bg-Base` gap.
- **Effect:** choosing one copies that preset into `Highlight`, `Highlight-Hover`, `Highlight-Text` and `Highlight-Soft` straight away, before Save, so the person sees the result. Cancel restores the previous choice.
- **Above it:** a "Highlight colour" `label-field` and a `hint`: "Colours the main buttons, the QP badge, selected rows, switches and the sort arrow."
- **Keyboard:** one radio group; arrow keys move the choice.
- **The consumer provides** the current preset and the change handler.
