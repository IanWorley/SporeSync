package com.sporesync.web

import org.springframework.stereotype.Controller
import org.springframework.web.bind.annotation.GetMapping

@Controller
class SpaFallbackController {
    @GetMapping("/settings", "/autoqueue", "/about")
    fun forwardClientRoutes(): String = "forward:/index.html"
}
