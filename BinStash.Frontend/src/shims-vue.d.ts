// Source - https://stackoverflow.com/a
// Posted by gg.jiang
// Retrieved 2025-12-25, License - CC BY-SA 4.0

declare module "*.vue" {
  import { defineComponent } from "vue";
  const Component: ReturnType<typeof defineComponent>;
  export default Component;
}
