// <auto-generated/>
#pragma warning disable 1591
namespace Microsoft.AspNetCore.Razor.Language.IntegrationTests.TestFiles
{
    #line hidden
    public class TestFiles_IntegrationTests_CodeGenerationIntegrationTest_TagHelpersWithTemplate_DesignTime
    {
        #line hidden
        #pragma warning disable 0649
        private global::Microsoft.AspNetCore.Razor.Runtime.TagHelpers.TagHelperExecutionContext __tagHelperExecutionContext;
        #pragma warning restore 0649
        private global::Microsoft.AspNetCore.Razor.Runtime.TagHelpers.TagHelperRunner __tagHelperRunner = new global::Microsoft.AspNetCore.Razor.Runtime.TagHelpers.TagHelperRunner();
        private global::DivTagHelper __DivTagHelper;
        private global::InputTagHelper __InputTagHelper;
        #pragma warning disable 219
        private void __RazorDirectiveTokenHelpers__() {
        ((System.Action)(() => {
#nullable restore
#line 1 "TestFiles/IntegrationTests/CodeGenerationIntegrationTest/TagHelpersWithTemplate.cshtml"
global::System.Object __typeHelper = "*, TestAssembly";

#line default
#line hidden
#nullable disable
        }
        ))();
        }
        #pragma warning restore 219
        #pragma warning disable 0414
        private static System.Object __o = null;
        #pragma warning restore 0414
        #pragma warning disable 1998
        public async System.Threading.Tasks.Task ExecuteAsync()
        {
#nullable restore
#line 13 "TestFiles/IntegrationTests/CodeGenerationIntegrationTest/TagHelpersWithTemplate.cshtml"
      
        RenderTemplate(
            "Template: ",
            

#line default
#line hidden
#nullable disable
            item => new Template(async(__razor_template_writer) => {
#nullable restore
#line 16 "TestFiles/IntegrationTests/CodeGenerationIntegrationTest/TagHelpersWithTemplate.cshtml"
                                  __o = item;

#line default
#line hidden
#nullable disable
                __InputTagHelper = CreateTagHelper<global::InputTagHelper>();
                await __tagHelperRunner.RunAsync(__tagHelperExecutionContext);
                __DivTagHelper = CreateTagHelper<global::DivTagHelper>();
                await __tagHelperRunner.RunAsync(__tagHelperExecutionContext);
            }
            )
#nullable restore
#line 16 "TestFiles/IntegrationTests/CodeGenerationIntegrationTest/TagHelpersWithTemplate.cshtml"
                                                                                               );
    

#line default
#line hidden
#nullable disable
            __DivTagHelper = CreateTagHelper<global::DivTagHelper>();
            await __tagHelperRunner.RunAsync(__tagHelperExecutionContext);
        }
        #pragma warning restore 1998
#nullable restore
#line 3 "TestFiles/IntegrationTests/CodeGenerationIntegrationTest/TagHelpersWithTemplate.cshtml"
            
    public void RenderTemplate(string title, Func<string, HelperResult> template)
    {
        Output.WriteLine("<br /><p><em>Rendering Template:</em></p>");
        var helperResult = template(title);
        helperResult.WriteTo(Output, HtmlEncoder);
    }

#line default
#line hidden
#nullable disable
    }
}
#pragma warning restore 1591
